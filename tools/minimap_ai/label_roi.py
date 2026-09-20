"""Công cụ gắn khung minimap cho từng ảnh ROI; chỉ đọc/ghi trên máy."""
import argparse
import json
import os
from pathlib import Path
import tkinter as tk
from tkinter import messagebox

from PIL import Image, ImageTk

DEFAULT_FOLDER = (
    Path(os.environ.get("LOCALAPPDATA", str(Path.home())))
    / "XerathSupportAssistant" / "minimap-ai-training"
)


class Labeler:
    def __init__(self, root: tk.Tk, folder: Path):
        self.root = root
        self.folder = folder
        self.images = sorted(folder.glob("roi-*.png"))
        self.index = 0
        self.start = None
        self.rectangle = None
        self.coords = None
        self.photo = None
        self.current = None
        self.shown_width = 0
        self.shown_height = 0
        root.title("Gắn nhãn minimap · AI cục bộ")
        tk.Label(root, text="Kéo chuột khoanh CHÍNH XÁC toàn bộ minimap. S = lưu; N = ảnh tiếp; P = ảnh trước.\n"
                 "Nếu ảnh không rõ: bỏ qua hoặc xóa ảnh khỏi thư mục. Không dùng khung đoán mò.").pack()
        self.canvas = tk.Canvas(root, width=920, height=560, bg="#222222")
        self.canvas.pack()
        self.canvas.bind("<ButtonPress-1>", self.mouse_down)
        self.canvas.bind("<B1-Motion>", self.mouse_move)
        self.canvas.bind("<ButtonRelease-1>", self.mouse_up)
        self.status = tk.Label(root, text="")
        self.status.pack()
        controls = tk.Frame(root)
        controls.pack(before=self.canvas)
        tk.Button(controls, text="Ảnh trước", command=self.prev).pack(side="left")
        tk.Button(controls, text="Lưu khung đã chọn", command=self.save).pack(side="left")
        tk.Button(controls, text="Ảnh tiếp", command=self.next).pack(side="left")
        root.bind("<Key-s>", lambda _: self.save())
        root.bind("<Key-n>", lambda _: self.next())
        root.bind("<Key-p>", lambda _: self.prev())
        root.bind("<Key-a>", lambda _: self.use_ai_suggestion())
        tk.Button(controls, text="Xem đề xuất AI (A)", command=self.use_ai_suggestion).pack(side="left")
        if not self.images:
            self.status.config(text=f"Không có ảnh roi-*.png trong {folder}")
        else:
            self.show()

    def show(self):
        self.current = self.images[self.index]
        with Image.open(self.current) as src:
            image = src.convert("RGB")
        self.original_width, self.original_height = image.size
        image.thumbnail((900, 530), Image.Resampling.LANCZOS)
        self.shown_width, self.shown_height = image.size
        self.photo = ImageTk.PhotoImage(image)
        self.canvas.delete("all")
        self.canvas.create_image(10, 10, anchor="nw", image=self.photo)
        self.rectangle = None
        self.coords = None
        label = self.current.with_suffix(".txt")
        if label.exists():
            try:
                items = label.read_text(encoding="utf-8").strip().split()
                if len(items) == 5 and items[0] == "0":
                    _, cx, cy, w, h = items
                    cx, cy, w, h = map(float, (cx, cy, w, h))
                    self.draw((cx - w/2)*self.shown_width, (cy - h/2)*self.shown_height,
                              (cx + w/2)*self.shown_width, (cy + h/2)*self.shown_height)
            except (ValueError, OSError):
                pass
        review = self.current.with_suffix(".ai-review.json")
        flagged = False
        if review.exists():
            try:
                flagged = json.loads(review.read_text(encoding="utf-8")).get("requiresManualReview") is True
            except (OSError, ValueError, TypeError):
                flagged = True
        self.status.config(text=f"Ảnh {self.index+1}/{len(self.images)}: {self.current.name} "
                           f"| {'ĐÃ GẮN NHÃN' if label.exists() else 'CHƯA CÓ NHÃN'} "
                           + ("| AI KHÁC NHÃN: nhấn A để xem đề xuất, kiểm tra rồi S" if flagged else ""))

    def use_ai_suggestion(self):
        if not self.current:
            return
        suggested = self.current.with_suffix(".ai-suggested.txt")
        if not suggested.exists():
            messagebox.showinfo("Không có đề xuất", "AI Cá Nhân chưa đề xuất được khung cho ảnh này.")
            return
        try:
            fields = suggested.read_text(encoding="utf-8").strip().split()
            if len(fields) != 5 or fields[0] != "0":
                raise ValueError("Sai định dạng tọa độ")
            cx, cy, w, h = map(float, fields[1:])
            if (not 0 < w <= 1 or not 0 < h <= 1 or cx-w/2 < 0 or
                    cy-h/2 < 0 or cx+w/2 > 1 or cy+h/2 > 1):
                raise ValueError("Khung ngoài ảnh")
            self.draw((cx-w/2)*self.shown_width, (cy-h/2)*self.shown_height,
                      (cx+w/2)*self.shown_width, (cy+h/2)*self.shown_height)
            self.status.config(text="Đang xem khung AI GỢI Ý (chưa xác minh). "
                                   "Hãy kiểm tra bốn cạnh, chỉnh lại nếu sai, rồi nhấn S để lưu.")
        except (OSError, ValueError):
            messagebox.showwarning("Lỗi đề xuất", "Không thể đọc tọa độ AI cho ảnh này.")

    def clamp(self, x, y):
        return (min(max(x-10, 0), self.shown_width),
                min(max(y-10, 0), self.shown_height))

    def draw(self, x1, y1, x2, y2):
        if self.rectangle is not None:
            self.canvas.delete(self.rectangle)
        self.rectangle = self.canvas.create_rectangle(
            10+x1, 10+y1, 10+x2, 10+y2, outline="#00ff66", width=3)
        self.coords = (min(x1, x2), min(y1, y2), max(x1, x2), max(y1, y2))

    def mouse_down(self, event):
        self.start = self.clamp(event.x, event.y)

    def mouse_move(self, event):
        if self.start is not None:
            x, y = self.clamp(event.x, event.y)
            self.draw(*self.start, x, y)

    def mouse_up(self, event):
        self.mouse_move(event)
        self.start = None

    def save(self):
        if not self.current or not self.coords:
            messagebox.showwarning("Chưa chọn", "Kéo chuột chọn chính xác toàn bộ minimap trước.")
            return
        x1, y1, x2, y2 = self.coords
        if x2 - x1 < 20 or y2 - y1 < 20:
            messagebox.showwarning("Sai khung", "Khung quá nhỏ; hãy chọn lại.")
            return
        cx = (x1+x2)/2/self.shown_width
        cy = (y1+y2)/2/self.shown_height
        w = (x2-x1)/self.shown_width
        h = (y2-y1)/self.shown_height
        if not all(0 <= value <= 1 for value in (cx, cy, w, h)):
            messagebox.showerror("Sai khung", "Khung nằm ngoài ảnh.")
            return
        path = self.current.with_suffix(".txt")
        path.write_text(f"0 {cx:.7f} {cy:.7f} {w:.7f} {h:.7f}\n", encoding="utf-8")
        review = self.current.with_suffix(".ai-review.json")
        if review.exists():
            try:
                report = json.loads(review.read_text(encoding="utf-8"))
                report["requiresManualReview"] = False
                report["verified"] = True
                report["reviewedManually"] = True
                review.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
            except (OSError, ValueError, TypeError):
                messagebox.showwarning("Lỗi kiểm tra", "Đã lưu nhãn, nhưng chưa cập nhật được trạng thái duyệt AI.")
        self.status.config(text=f"Đã lưu và xác nhận nhãn minimap vào {path.name}")

    def next(self):
        if self.images:
            self.index = (self.index+1) % len(self.images)
            self.show()

    def prev(self):
        if self.images:
            self.index = (self.index-1) % len(self.images)
            self.show()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Gắn nhãn thủ công minimap trong ảnh ROI")
    parser.add_argument("--images", type=Path, default=DEFAULT_FOLDER)
    arguments = parser.parse_args()
    root = tk.Tk()
    Labeler(root, arguments.images).root.mainloop()
