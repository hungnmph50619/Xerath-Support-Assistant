"""Huấn luyện classifier biểu tượng tướng trên minimap từ các điểm gắn nhãn thủ công."""
import argparse
import hashlib
import json
import os
import random
import re
import shutil
from pathlib import Path

from PIL import Image

DEFAULT_SOURCE = (
    Path(os.environ.get("LOCALAPPDATA", str(Path.home())))
    / "XerathSupportAssistant" / "minimap-labeled-samples-v1.8"
)
DEFAULT_MODEL = (
    Path(os.environ.get("LOCALAPPDATA", str(Path.home())))
    / "XerathSupportAssistant" / "models" / "champion-icon-classifier.onnx"
)
DEFAULT_LABELS = DEFAULT_MODEL.with_name("champion-icon-labels.json")
INPUT_SIZE = 64
PATCH_FRACTION = 0.11
SAFE = re.compile(r"[^a-z0-9_-]+")


def slug(value: str) -> str:
    return SAFE.sub("-", value.strip().lower()).strip("-") or "unknown"


def class_key(team: str, champion: str) -> str:
    return f"{team}__{slug(champion)}"


def load_sample(meta_path: Path):
    data = json.loads(meta_path.read_text(encoding="utf-8"))
    if data.get("evidenceKind") != "visible-observation":
        return None
    marks = data.get("marks")
    if not isinstance(marks, list) or not marks:
        return None
    image_path = meta_path.with_suffix(".jpg")
    if not image_path.exists():
        return None
    return image_path, data


def split_for(data: dict, sample_name: str) -> str:
    existing = data.get("datasetSplit")
    if existing == "test":
        return "test"
    group = data.get("sequenceGroup") or sample_name
    digest = hashlib.sha256(group.encode("utf-8")).digest()[0]
    return "val" if digest % 5 == 0 else "train"


def crop_patch(image: Image.Image, x: float, y: float, patch_fraction: float):
    w, h = image.size
    patch = max(18, min(96, round(min(w, h) * patch_fraction)))
    cx = round(x * w)
    cy = round(y * h)
    half = patch // 2
    left = max(0, min(w - patch, cx - half))
    top = max(0, min(h - patch, cy - half))
    return image.crop((left, top, left + patch, top + patch)).resize(
        (INPUT_SIZE, INPUT_SIZE), Image.Resampling.BILINEAR
    )


def distance_sq(ax, ay, bx, by):
    return (ax - bx) ** 2 + (ay - by) ** 2


def prepare_dataset(source: Path, destination: Path, seed: int):
    if destination.exists():
        raise SystemExit(
            f"{destination} đã tồn tại. Dùng --workspace mới để tránh trộn dữ liệu."
        )

    manifests = {}
    positive_count = 0
    background_count = 0
    rng = random.Random(seed)

    for meta_path in sorted(source.glob("sample-*.json")):
        loaded = load_sample(meta_path)
        if loaded is None:
            continue

        image_path, data = loaded
        sample_name = meta_path.stem
        split = split_for(data, sample_name)

        try:
            image = Image.open(image_path).convert("RGB")
        except OSError:
            continue

        marks = []
        for idx, raw in enumerate(data.get("marks", [])):
            try:
                x = float(raw["x"])
                y = float(raw["y"])
                champion = str(raw["champion"]).strip()
                team = str(raw["team"]).strip().lower()
            except (KeyError, TypeError, ValueError):
                continue
            if not (0 <= x <= 1 and 0 <= y <= 1):
                continue
            if team not in {"ally", "enemy"} or len(champion) < 2:
                continue

            key = class_key(team, champion)
            manifests[key] = {"champion": champion, "team": team}
            folder = destination / split / key
            folder.mkdir(parents=True, exist_ok=True)
            patch = crop_patch(image, x, y, PATCH_FRACTION)
            patch.save(folder / f"{sample_name}-{idx:02d}.jpg", quality=95)
            positive_count += 1
            marks.append((x, y))

        if not marks:
            image.close()
            continue

        # Background examples: deterministic-random locations kept away from all marks.
        target_background = min(4, max(2, len(marks)))
        attempts = 0
        saved = 0
        while saved < target_background and attempts < 80:
            attempts += 1
            x = rng.uniform(0.08, 0.92)
            y = rng.uniform(0.08, 0.92)
            if any(distance_sq(x, y, mx, my) < 0.08 ** 2 for mx, my in marks):
                continue
            folder = destination / split / "background"
            folder.mkdir(parents=True, exist_ok=True)
            patch = crop_patch(image, x, y, PATCH_FRACTION)
            patch.save(folder / f"{sample_name}-bg-{saved:02d}.jpg", quality=95)
            background_count += 1
            saved += 1

        image.close()

    if positive_count < 30:
        raise SystemExit(
            f"Mới tạo được {positive_count} patch biểu tượng. Cần ít nhất 30 patch "
            "đã gắn nhãn chỉ để thử pipeline; nên có nhiều hơn cho từng tướng/đội."
        )
    if len(manifests) < 2:
        raise SystemExit("Cần ít nhất hai lớp tướng/đội khác nhau để thử classifier.")
    if background_count < 10:
        raise SystemExit("Chưa đủ patch nền để học lớp background.")

    manifest_path = destination / "class-manifest.json"
    manifest_path.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "inputSize": INPUT_SIZE,
                "classes": manifests,
                "background": {"champion": "", "team": "unknown"},
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    print(
        f"Đã chuẩn bị {positive_count} patch biểu tượng và "
        f"{background_count} patch background."
    )
    return manifest_path


def main():
    parser = argparse.ArgumentParser(
        description="Huấn luyện local classifier nhận diện biểu tượng tướng minimap"
    )
    parser.add_argument("--samples", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--workspace", type=Path, default=Path("champion-icon-work"))
    parser.add_argument("--output", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--labels-output", type=Path, default=DEFAULT_LABELS)
    parser.add_argument("--base-model", default="yolov8n-cls.pt")
    parser.add_argument("--epochs", type=int, default=50)
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    if args.epochs < 1 or args.epochs > 300:
        raise SystemExit("--epochs phải từ 1 đến 300")
    if not args.samples.exists():
        raise SystemExit(f"Không thấy thư viện mẫu: {args.samples}")

    workspace = args.workspace.resolve()
    dataset = workspace / "dataset"
    manifest_path = prepare_dataset(args.samples, dataset, args.seed)

    from ultralytics import YOLO

    model = YOLO(args.base_model)
    model.train(
        data=str(dataset),
        imgsz=INPUT_SIZE,
        epochs=args.epochs,
        device=args.device,
        workers=0,
        batch=32,
        seed=args.seed,
        project=str(workspace / "runs"),
        name="champion-icons",
        exist_ok=False,
    )

    best = workspace / "runs" / "champion-icons" / "weights" / "best.pt"
    if not best.exists():
        raise SystemExit(f"Không tìm thấy mô hình đã học: {best}")

    final = YOLO(str(best))
    exported = Path(
        final.export(
            format="onnx",
            imgsz=INPUT_SIZE,
            opset=12,
            dynamic=True,
            simplify=False,
        )
    )
    if not exported.exists():
        raise SystemExit("Huấn luyện xong nhưng không tìm thấy ONNX.")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    safe_manifest = dict(manifest["classes"])
    safe_manifest["background"] = manifest["background"]

    names = final.names
    classes = []
    for index in range(len(names)):
        key = str(names[index])
        if key not in safe_manifest:
            raise SystemExit(
                f"Lớp model '{key}' không tồn tại trong class-manifest.json."
            )
        item = safe_manifest[key]
        classes.append(
            {
                "index": index,
                "key": key,
                "champion": item["champion"],
                "team": item["team"],
            }
        )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.labels_output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(exported, args.output)
    args.labels_output.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "inputSize": INPUT_SIZE,
                "classes": classes,
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    print(f"Đã xuất model: {args.output}")
    print(f"Đã xuất nhãn lớp: {args.labels_output}")
    print(
        "Lưu ý: classifier chỉ nhận diện những lớp có dữ liệu gắn nhãn. "
        "Phải đánh giá riêng trên thư mục test trước khi dùng làm tín hiệu coach."
    )


if __name__ == "__main__":
    main()
