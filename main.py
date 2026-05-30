import webview
import os
import sys
from pathlib import Path

# Добавляем папку backend в пути поиска, чтобы импорты работали корректно
sys.path.append(os.path.join(os.path.dirname(__file__), 'backend'))

# Импортируем твои существующие модули
from db import get_conn, init_db
from scanner import scan as scan_clips
from media import thumb_path

class KeepClipApi:
    """Этот класс будет доступен в JavaScript как window.pywebview.api"""
    
    def __init__(self):
        self.window = None # Ссылка на окно (заполним позже)

    def ping(self):
        return {"ok": True, "message": "API работает напрямую!"}

    def scan_folder(self):
        """Заменяет @app.post('/api/scan')"""
        result = scan_clips()
        return result

    def get_clips(self, sort="newest", limit=200):
        """Заменяет @app.get('/api/clips')"""
        order_map = {"newest": "mtime DESC", "oldest": "mtime ASC", "largest": "size_bytes DESC", "smallest": "size_bytes ASC"}
        order = order_map.get(sort, "mtime DESC")
        
        with get_conn() as con:
            rows = con.execute(
                f"SELECT id, game, filename, duration, size_bytes, mtime, has_thumb, transcribed_at, favorite "
                f"FROM clips ORDER BY {order} LIMIT ?", 
                (limit,)
            ).fetchall()
            return [dict(r) for r in rows]

if __name__ == '__main__':
    # 1. Инициализируем базу данных (как было в app.py при startup)
    init_db()
    
    # 2. Создаем наш API-мост
    api = KeepClipApi()
    
    # 3. Путь к твоему index.html
    html_path = os.path.join(os.path.dirname(__file__), 'frontend', 'index.html')
    
    # 4. Создаем нативное окно
    window = webview.create_window(
        title='KeepClip', 
        url=html_path, 
        js_api=api,
        width=1280, 
        height=800,
        min_size=(900, 600)
    )
    api.window = window
    
    # 5. Запускаем приложение
    webview.start(debug=True)