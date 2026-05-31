import webview
import threading
import uvicorn
import socket
import sys
import os

# 1. Корневая папка проекта (где лежат data и bin)
root_dir = os.path.dirname(os.path.abspath(__file__))
os.chdir(root_dir)

# 2. Добавляем bin в системный PATH (чтобы ffmpeg резал миниатюры)
bin_dir = os.path.join(root_dir, 'bin')
os.environ["PATH"] = bin_dir + os.pathsep + os.environ.get("PATH", "")

# 3. Подключаем backend, чтобы импорты сработали
backend_dir = os.path.join(root_dir, 'backend')
sys.path.append(backend_dir)

# Теперь безопасно импортируем приложение (оно само настроит статику)
from app import app, init_db

def get_free_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(('127.0.0.1', 0))
    port = s.getsockname()[1]
    s.close()
    return port

def start_hidden_server(port):
    uvicorn.run(app, host="127.0.0.1", port=port, log_level="critical")

class WindowApi:
    def __init__(self):
        self._window = None
        self._is_maximized = False
        self._restore_width = 1280
        self._restore_height = 800

    def close_window(self):
        if self._window: self._window.destroy()

    def minimize_window(self):
        if self._window: self._window.minimize()

    def pick_folder(self):
        if not self._window:
            return None
        # Открываем системный диалог выбора папки
        result = self._window.create_file_dialog(webview.FOLDER_DIALOG)
        if result and len(result) > 0:
            return result[0] # Возвращаем выбранный путь
        return None

    def start_native_resize(self):
        import ctypes
        user32 = ctypes.windll.user32
        hwnd = user32.FindWindowW(None, 'KeepClip')
        if hwnd:
            user32.ReleaseCapture()
            user32.PostMessageW(hwnd, 0x00A1, 17, 0)

    def toggle_maximize_window(self, avail_width, avail_height, avail_left, avail_top):
        if not self._window: return
        if self._is_maximized:
            self._window.resize(self._restore_width, self._restore_height)
            self._is_maximized = False
        else:
            try:
                self._restore_width, self._restore_height = self._window.width, self._window.height
            except: pass
            self._window.resize(avail_width, avail_height)
            self._window.move(avail_left, avail_top)
            self._is_maximized = True

if __name__ == '__main__':
    init_db()
    port = get_free_port()
    threading.Thread(target=start_hidden_server, args=(port,), daemon=True).start()

    api = WindowApi()
    window = webview.create_window(
        title='KeepClip', 
        url=f'http://127.0.0.1:{port}', 
        width=1280, height=800,
        min_size=(900, 600),
        frameless=True,
        resizable=True,
        js_api=api
    )
    api._window = window

    icon_path = os.path.join(root_dir, 'frontend', 'icons', 'icon.ico')

    webview.start(icon=icon_path)