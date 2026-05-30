"""Static config — paths and runtime knobs."""
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
DATA_DIR = PROJECT_ROOT / "data"
THUMBS_DIR = DATA_DIR / "thumbs"
TOOLS_BIN = PROJECT_ROOT / "tools" / "bin"
FFMPEG = TOOLS_BIN / "ffmpeg.exe"
FFPROBE = TOOLS_BIN / "ffprobe.exe"

# Compile-time fallback for the clips folder. The user can override this at
# runtime via the in-app settings (settings.py), which is what scanner.py reads.
# Defaults to the typical NVIDIA ShadowPlay location under the current user.
DEFAULT_CLIPS_ROOT = Path.home() / "Videos" / "NVIDIA"

# Cut/compressed fragments are saved in this subfolder of the clips root, so they
# sit alongside the game folders and get picked up by the scanner (accessible in-app).
CUTS_SUBDIR = "Wycinki"

VIDEO_EXTS = {".mp4", ".mkv", ".mov", ".avi", ".webm"}

# Whisper config
WHISPER_MODEL = "large-v3-turbo"
WHISPER_LANG = "pl"
WHISPER_DEVICE = "cuda"
WHISPER_COMPUTE_TYPE = "int8"  # GTX 1080 Ti (Pascal) has no efficient fp16; int8 is fast + accurate on Pascal CUDA

# Word-level Whisper timestamps via DTW are far more accurate than the default
# attention-based ones, especially across long silent stretches (e.g. walking
# on the map without talking). We use them to set each segment's start/end to
# the actual first/last spoken word, and to split segments whenever there is
# a long enough internal pause that the words almost certainly aren't related.
WHISPER_SPLIT_GAP_SECONDS = 1.5
