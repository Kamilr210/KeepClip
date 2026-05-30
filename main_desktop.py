import webview
import threading
import uvicorn
import socket
import sys
import os

# Получаем путь к папке, где лежит main_desktop.py
base_path = os.path.dirname(os.path.abspath(__file__))

# Добавляем backend в sys.path
sys.path.append(os.path.join(base_path, 'backend'))

from app import app, init_db

# Настраиваем FastAPI на поиск фронтенда и статики в правильных папках
# Это критически важно, чтобы 404 исчезли
frontend_dir = os.path.join(base_path, 'frontend')
app.mount("/static", __import__("fastapi.staticfiles").staticfiles.StaticFiles(directory=frontend_dir), name="static")

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
    # Устанавливаем рабочую директорию в backend, чтобы база и ffmpeg были доступны
    os.chdir(os.path.join(base_path, 'backend'))
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
    webview.start()