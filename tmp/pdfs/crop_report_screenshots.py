from pathlib import Path

from PIL import Image


RAW_DIR = Path(r"D:\Project\AI_WebApi\tmp\pdfs\raw_screenshots")
OUTPUT_DIR = Path(r"C:\Users\weidaohao\Desktop\数据中心AI分析固定演示Workflow_v1.0\局部报表_v1.0")


def color_distance(left, right):
    return sum(abs(int(a) - int(b)) for a, b in zip(left[:3], right[:3]))


def crop_report(source: Path):
    with Image.open(source).convert("RGB") as image:
        background = image.getpixel((5, 5))
        center_x = image.width // 2
        last_content_y = 0
        for y in range(image.height):
            if color_distance(image.getpixel((center_x, y)), background) > 12:
                last_content_y = y

        crop_height = min(image.height, last_content_y + 33)
        if crop_height < 500:
            raise RuntimeError(f"Unexpected crop height for {source.name}: {crop_height}")
        cropped = image.crop((0, 0, image.width, crop_height))
        target = OUTPUT_DIR / source.name
        cropped.save(target, optimize=True)
        print(f"{source.name}: {image.width}x{image.height} -> {cropped.width}x{cropped.height}")


def main():
    sources = sorted(RAW_DIR.glob("data-center-report*.png"))
    if len(sources) != 9:
        raise RuntimeError(f"Expected 9 raw screenshots, found {len(sources)}")
    for source in sources:
        crop_report(source)


if __name__ == "__main__":
    main()
