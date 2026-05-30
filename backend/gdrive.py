"""Google Drive backend: optional cloud offload for clips.

Design notes:
- Scope is `drive.file`, so the app only ever sees files IT created (a single
  "KeepClip" folder) — it can never read the rest of the user's Drive.
- OAuth tokens live in the OS keychain (Windows Credential Manager) via keyring,
  never in plaintext on disk.
- There is no central KeepClip server. The user supplies their OWN OAuth client
  by dropping the downloaded `google_client.json` into the data folder, so this
  stays a pure BYO-cloud, open-source-friendly setup.
- Large clips are uploaded with a chunked *resumable* session (Drive requires it
  above 5 MB) and streamed back with HTTP Range pass-through so the <video>
  element can seek.
"""
from __future__ import annotations

import json
import os
import threading
from pathlib import Path
from typing import Optional

import requests

# Google may grant extra scopes (e.g. openid) alongside the drive.file we ask
# for; relax oauthlib's strict scope-equality check so that doesn't raise. Must
# be set before importing the oauthlib-based flow below.
os.environ.setdefault("OAUTHLIB_RELAX_TOKEN_SCOPE", "1")

import keyring
from keyring.errors import PasswordDeleteError
from google.auth.transport.requests import Request as GoogleAuthRequest
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import Flow

from config import DATA_DIR

CLIENT_SECRET_PATH = DATA_DIR / "google_client.json"
SCOPES = ["https://www.googleapis.com/auth/drive.file"]
REDIRECT_URI = "http://127.0.0.1:8765/api/cloud/oauth2callback"
APP_FOLDER_NAME = "KeepClip"

_KEYRING_SERVICE = "KeepClip"
_KEYRING_USER = "google_drive_token"

_DRIVE_FILES = "https://www.googleapis.com/drive/v3/files"
_DRIVE_UPLOAD = "https://www.googleapis.com/upload/drive/v3/files"
_DRIVE_ABOUT = "https://www.googleapis.com/drive/v3/about"

# Resumable upload chunk size — must be a multiple of 256 KiB.
_UPLOAD_CHUNK = 16 * 1024 * 1024

_lock = threading.Lock()
_app_folder_id: Optional[str] = None
# PKCE: google-auth-oauthlib auto-generates a code_verifier when we build the auth
# URL and sends its challenge to Google. The SAME verifier must be presented at the
# token-exchange step or Google rejects it with "Missing code verifier". Since the
# two steps create separate Flow objects, we stash the verifier here in between.
_pending_code_verifier: Optional[str] = None


class GDriveError(RuntimeError):
    """Any Drive operation that failed in a way worth showing the user."""


class NotConnected(GDriveError):
    """Raised when an operation needs a connected account but there isn't one."""


# ---------- client config / connection state ----------
def is_configured() -> bool:
    """True once the user has dropped their OAuth client json into the data folder."""
    return CLIENT_SECRET_PATH.exists()


def _flow() -> Flow:
    if not is_configured():
        raise GDriveError(
            "Brak pliku data/google_client.json — utwórz klienta OAuth w Google Cloud "
            "i pobierz go do folderu data."
        )
    return Flow.from_client_secrets_file(
        str(CLIENT_SECRET_PATH), scopes=SCOPES, redirect_uri=REDIRECT_URI
    )


# ---------- token storage (OS keychain) ----------
def _save_creds(creds: Credentials) -> None:
    keyring.set_password(_KEYRING_SERVICE, _KEYRING_USER, creds.to_json())


def _load_creds() -> Optional[Credentials]:
    raw = keyring.get_password(_KEYRING_SERVICE, _KEYRING_USER)
    if not raw:
        return None
    try:
        creds = Credentials.from_authorized_user_info(json.loads(raw), SCOPES)
    except Exception:
        return None
    if creds and creds.expired and creds.refresh_token:
        try:
            creds.refresh(GoogleAuthRequest())
            _save_creds(creds)
        except Exception:
            return None
    return creds if (creds and creds.valid) else None


def is_connected() -> bool:
    with _lock:
        return _load_creds() is not None


def disconnect() -> None:
    global _app_folder_id
    _app_folder_id = None
    try:
        keyring.delete_password(_KEYRING_SERVICE, _KEYRING_USER)
    except PasswordDeleteError:
        pass


# ---------- OAuth dance ----------
def build_auth_url() -> str:
    """URL the user opens to grant access. prompt=consent + offline guarantees a
    refresh token so we can keep the connection alive without re-login."""
    global _pending_code_verifier
    flow = _flow()
    url, _state = flow.authorization_url(
        access_type="offline", include_granted_scopes="true", prompt="consent"
    )
    # Remember the PKCE verifier this URL committed to, for the exchange step.
    _pending_code_verifier = flow.code_verifier
    return url


def exchange_code(code: str) -> None:
    global _pending_code_verifier
    flow = _flow()
    # Re-attach the verifier from build_auth_url so the PKCE challenge/verifier pair
    # matches; without it Google returns "invalid_grant: Missing code verifier".
    if _pending_code_verifier:
        flow.code_verifier = _pending_code_verifier
    flow.fetch_token(code=code)
    _save_creds(flow.credentials)
    _pending_code_verifier = None


# ---------- authed HTTP helpers ----------
def _token() -> str:
    with _lock:
        creds = _load_creds()
    if not creds:
        raise NotConnected("Nie połączono z Google Drive.")
    return creds.token


def _auth_headers() -> dict:
    return {"Authorization": f"Bearer {_token()}"}


# ---------- account / usage ----------
def account_info() -> dict:
    r = requests.get(
        _DRIVE_ABOUT,
        headers=_auth_headers(),
        params={"fields": "user(displayName,emailAddress,photoLink),storageQuota"},
        timeout=20,
    )
    if r.status_code != 200:
        raise GDriveError(f"Drive about.get: {r.status_code} {r.text[:200]}")
    data = r.json()
    user = data.get("user", {})
    quota = data.get("storageQuota", {})

    def _int(v):
        try:
            return int(v)
        except (TypeError, ValueError):
            return None

    return {
        "email": user.get("emailAddress"),
        "name": user.get("displayName"),
        "photo": user.get("photoLink"),
        "usage": _int(quota.get("usage")),
        "limit": _int(quota.get("limit")),  # None => unlimited (e.g. Workspace)
    }


def status() -> dict:
    """Everything the Cloud view needs in one call."""
    if not is_configured():
        return {"configured": False, "connected": False}
    if not is_connected():
        return {"configured": True, "connected": False}
    try:
        return {"configured": True, "connected": True, **account_info()}
    except Exception as e:  # connected but Drive call failed — still report connected
        return {"configured": True, "connected": True, "error": str(e)}


# ---------- the app's own folder ----------
def _ensure_app_folder() -> str:
    global _app_folder_id
    if _app_folder_id:
        return _app_folder_id
    headers = _auth_headers()
    q = (
        f"name='{APP_FOLDER_NAME}' and "
        "mimeType='application/vnd.google-apps.folder' and trashed=false"
    )
    r = requests.get(
        _DRIVE_FILES,
        headers=headers,
        params={"q": q, "fields": "files(id,name)", "spaces": "drive"},
        timeout=20,
    )
    if r.status_code == 200 and r.json().get("files"):
        _app_folder_id = r.json()["files"][0]["id"]
        return _app_folder_id
    r = requests.post(
        _DRIVE_FILES,
        headers={**headers, "Content-Type": "application/json"},
        params={"fields": "id"},
        data=json.dumps(
            {"name": APP_FOLDER_NAME, "mimeType": "application/vnd.google-apps.folder"}
        ),
        timeout=20,
    )
    if r.status_code not in (200, 201):
        raise GDriveError(
            f"Nie udało się utworzyć folderu KeepClip: {r.status_code} {r.text[:200]}"
        )
    _app_folder_id = r.json()["id"]
    return _app_folder_id


# ---------- upload (chunked resumable) ----------
def _put_chunk(session_uri: str, chunk: bytes, headers: dict, retries: int = 3):
    last_exc = None
    for _ in range(retries):
        try:
            return requests.put(session_uri, data=chunk, headers=headers, timeout=300)
        except requests.RequestException as e:
            last_exc = e
    raise GDriveError(f"Błąd sieci podczas wysyłania: {last_exc}")


def upload(local_path: Path, name: Optional[str] = None) -> str:
    """Upload a file to the KeepClip folder. Returns the Drive file id."""
    local_path = Path(local_path)
    if not local_path.exists():
        raise GDriveError(f"Plik nie istnieje: {local_path}")
    name = name or local_path.name
    total = local_path.stat().st_size
    folder_id = _ensure_app_folder()

    # 1) open a resumable session
    r = requests.post(
        _DRIVE_UPLOAD,
        headers={**_auth_headers(), "Content-Type": "application/json; charset=UTF-8"},
        params={"uploadType": "resumable", "fields": "id"},
        data=json.dumps({"name": name, "parents": [folder_id]}),
        timeout=30,
    )
    if r.status_code not in (200, 201):
        raise GDriveError(f"Start wysyłania nieudany: {r.status_code} {r.text[:200]}")
    session_uri = r.headers.get("Location")
    if not session_uri:
        raise GDriveError("Brak adresu sesji wysyłania od Google.")

    # 2) push the bytes; the session URI is pre-authorized so chunk PUTs carry no
    #    Authorization header. 308 = Resume Incomplete; 200/201 = done.
    if total == 0:
        fin = requests.put(session_uri, headers={"Content-Range": "bytes */0"}, timeout=60)
        if fin.status_code not in (200, 201):
            raise GDriveError(f"Wysyłanie pustego pliku nieudane: {fin.status_code}")
        return fin.json()["id"]

    offset = 0
    with open(local_path, "rb") as f:
        while offset < total:
            f.seek(offset)
            chunk = f.read(_UPLOAD_CHUNK)
            end = offset + len(chunk) - 1
            resp = _put_chunk(
                session_uri,
                chunk,
                {
                    "Content-Length": str(len(chunk)),
                    "Content-Range": f"bytes {offset}-{end}/{total}",
                },
            )
            if resp.status_code in (200, 201):
                return resp.json()["id"]
            if resp.status_code == 308:
                rng = resp.headers.get("Range")
                # Range: bytes=0-N — resume just past what Drive confirmed it has.
                offset = int(rng.split("-")[-1]) + 1 if rng and "-" in rng else end + 1
                continue
            raise GDriveError(f"Wysyłanie nieudane: {resp.status_code} {resp.text[:200]}")
    raise GDriveError("Wysyłanie zakończone bez potwierdzenia od Google.")


# ---------- download (ranged streaming proxy source) ----------
def open_range_stream(file_id: str, range_header: Optional[str]) -> requests.Response:
    """Open a streaming GET to Drive, forwarding the browser's Range header.

    Returns the live `requests.Response` (stream=True); the caller iterates over
    it and MUST close it. acknowledgeAbuse skips Drive's large-file scan warning.
    """
    headers = _auth_headers()
    if range_header:
        headers["Range"] = range_header
    r = requests.get(
        f"{_DRIVE_FILES}/{file_id}",
        headers=headers,
        params={"alt": "media", "acknowledgeAbuse": "true"},
        stream=True,
        timeout=60,
    )
    if r.status_code not in (200, 206):
        body = r.text[:200]
        r.close()
        raise GDriveError(f"Pobieranie z Drive nieudane: {r.status_code} {body}")
    return r


# ---------- download (full file to disk) ----------
def download(file_id: str, dest: Path) -> None:
    """Stream a whole Drive file down to `dest`. Writes to a sibling `.part` file
    first and renames on success, so an interrupted download can't leave a
    half-written clip in place. acknowledgeAbuse skips Drive's large-file scan."""
    dest = Path(dest)
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.parent / (dest.name + ".part")
    r = requests.get(
        f"{_DRIVE_FILES}/{file_id}",
        headers=_auth_headers(),
        params={"alt": "media", "acknowledgeAbuse": "true"},
        stream=True,
        timeout=300,
    )
    if r.status_code != 200:
        body = r.text[:200]
        r.close()
        raise GDriveError(f"Pobieranie z Drive nieudane: {r.status_code} {body}")
    try:
        with open(tmp, "wb") as f:
            for chunk in r.iter_content(chunk_size=1024 * 1024):
                if chunk:
                    f.write(chunk)
    except OSError as e:
        raise GDriveError(f"Zapis pliku nieudany: {e}")
    finally:
        r.close()
    tmp.replace(dest)  # atomic on the same volume


# ---------- delete ----------
def delete(file_id: str, permanent: bool = False) -> None:
    """Remove a clip from Drive. By default it goes to Drive trash (recoverable),
    mirroring how local deletes use the Recycle Bin."""
    if permanent:
        r = requests.delete(f"{_DRIVE_FILES}/{file_id}", headers=_auth_headers(), timeout=30)
        ok = r.status_code in (200, 204)
    else:
        r = requests.patch(
            f"{_DRIVE_FILES}/{file_id}",
            headers={**_auth_headers(), "Content-Type": "application/json"},
            data=json.dumps({"trashed": True}),
            timeout=30,
        )
        ok = r.status_code == 200
    if not ok:
        raise GDriveError(f"Usuwanie z Drive nieudane: {r.status_code} {r.text[:200]}")
