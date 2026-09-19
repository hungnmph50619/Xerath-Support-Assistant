"""Huấn luyện YOLO một lớp cho vùng minimap trên ảnh ROI và xuất ONNX dùng trên máy."""
import argparse
import json
import os
import random
import shutil
from pathlib import Path

DEFAULT_SOURCE = (
    Path(os.environ.get("LOCALAPPDATA", str(Path.home())))
    / "XerathSupportAssistant" / "minimap-ai-training"
)
DEFAULT_OUTPUT = (
    Path(os.environ.get("LOCALAPPDATA", str(Path.home())))
    / "XerathSupportAssistant" / "models" / "minimap-detector.onnx"
)


def valid_label(path: Path):
    try:
        rows = path.read_text(encoding="utf-8").strip().splitlines()
        if len(rows) != 1:
            return False
        parts = rows[0].split()
        if len(parts) != 5 or parts[0] != "0":
            return False
        cx, cy, w, h = map(float, parts[1:])
        return (0 < w <= 1 and 0 < h <= 1 and 0 <= cx-w/2 and cx+w/2 <= 1
                and 0 <= cy-h/2 and cy+h/2 <= 1)
    except (ValueError, OSError):
        return False


def prepare_dataset(source: Path, destination: Path, seed: int):
    images = [
        p for p in sorted(source.glob("roi-*.png"))
        if valid_label(p.with_suffix(".txt"))
    ]
    if len(images) < 30:
        raise SystemExit(
            f"Mới có {len(images)} ảnh gắn nhãn hợp lệ. Cần ít nhất 30 ảnh nhiều tình huống "
            "để thử huấn luyện, nên có hàng trăm ảnh đa dạng để đánh giá thực tế."
        )
    random.Random(seed).shuffle(images)
    val_count = max(5, round(len(images) * .2))
    splits = {"val": images[:val_count], "train": images[val_count:]}
    for split, files in splits.items():
        for image in files:
            image_folder = destination / split / "images"
            label_folder = destination / split / "labels"
            image_folder.mkdir(parents=True, exist_ok=True)
            label_folder.mkdir(parents=True, exist_ok=True)
            shutil.copy2(image, image_folder / image.name)
            shutil.copy2(image.with_suffix(".txt"), label_folder / image.with_suffix(".txt").name)
    yaml_path = destination / "dataset.yaml"
    yaml_path.write_text(
        "path: " + json.dumps(destination.resolve().as_posix()) + "\n"
        "train: train/images\n"
        "val: val/images\n"
        "names:\n"
        "  0: minimap\n",
        encoding="utf-8",
    )
    print(f"Tập học: {len(splits['train'])}, tập kiểm tra nội bộ: {len(splits['val'])}.")
    print("Lưu ý: các ảnh liên tiếp rất giống nhau làm kết quả kiểm tra quá lạc quan. "
          "Hãy giữ thêm một bộ ảnh từ trận/ngày/độ phân giải khác để đánh giá độc lập.")
    return yaml_path


def main():
    parser = argparse.ArgumentParser(description="Huấn luyện local AI nhận diện minimap")
    parser.add_argument("--images", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--workspace", type=Path, default=Path("minimap-ai-work"))
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--base-model", default="yolov8n.pt",
                        help="Tệp trọng số YOLOv8n; lần đầu Ultralytics có thể tải từ Internet")
    parser.add_argument("--epochs", type=int, default=60)
    parser.add_argument("--device", default="cpu",
                        help="cpu hoặc 0 nếu đã cấu hình CUDA tương thích")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    from ultralytics import YOLO

    if args.epochs < 1 or args.epochs > 300:
        raise SystemExit("--epochs phải từ 1 đến 300")
    workspace = args.workspace.resolve()
    # Không xóa hoặc ghi đè dữ liệu đã gắn nhãn. Dùng workspace mới cho mỗi lần học.
    dataset = workspace / "dataset"
    if dataset.exists():
        raise SystemExit(f"{dataset} đã tồn tại. Chọn --workspace mới để tránh trộn dữ liệu.")
    yaml_path = prepare_dataset(args.images, dataset, args.seed)
    detector = YOLO(args.base_model)
    detector.train(
        data=str(yaml_path), imgsz=640, epochs=args.epochs,
        device=args.device, workers=0, batch=8, seed=args.seed,
        project=str(workspace / "runs"), name="minimap", exist_ok=False,
        single_cls=True,
    )
    best = workspace / "runs" / "minimap" / "weights" / "best.pt"
    if not best.exists():
        raise SystemExit(f"Không tìm thấy mô hình đã học: {best}")
    final = YOLO(str(best))
    exported = Path(final.export(
        format="onnx", imgsz=640, opset=12, dynamic=False,
        simplify=False, nms=False
    ))
    if not exported.exists():
        raise SystemExit("Huấn luyện xong nhưng không tìm thấy ONNX; chưa cài mô hình.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(exported, args.output)
    print(f"Đã xuất mô hình AI để ứng dụng dùng trên máy: {args.output}")
    print("Mở lại ứng dụng rồi thử Tự tìm bằng AI. Luôn xem ảnh trước khi dùng khung.")


if __name__ == "__main__":
    main()
