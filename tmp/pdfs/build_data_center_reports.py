from __future__ import annotations

import copy
from pathlib import Path

from bs4 import BeautifulSoup


SOURCE = Path(r"C:\Users\weidaohao\Desktop\数据中心AI分析固定演示Workflow_v1.0\data-center-report.html")
OUTPUT_DIR = Path(r"C:\Users\weidaohao\Desktop\数据中心AI分析固定演示Workflow_v1.0\局部报表_v1.0")


REPORTS = {
    "full": {
        "filename": "data-center-report",
        "range": "全平台 + 岳阳医院",
        "subtitle": "智联康复元宇宙 · 全平台与岳阳医院运营分析",
        "sections": ["01", "02", "03", "04", "05", "06"],
        "height": 2700,
    },
    "platform": {
        "filename": "data-center-report-platform",
        "range": "全平台运营概览",
        "subtitle": "智联康复元宇宙 · 全平台运营概览",
        "sections": ["01", "02"],
        "height": 1100,
    },
    "institution": {
        "filename": "data-center-report-institution",
        "range": "岳阳医院运营详情",
        "subtitle": "智联康复元宇宙 · 岳阳医院运营详情",
        "sections": ["03", "04"],
        "height": 1050,
    },
    "consultation": {
        "filename": "data-center-report-consultation",
        "range": "全平台与岳阳医院问诊情况",
        "subtitle": "智联康复元宇宙 · 全平台与岳阳医院问诊情况",
        "sections": ["consultation"],
        "height": 750,
    },
    "rooms": {
        "filename": "data-center-report-rooms",
        "range": "虚拟诊室",
        "subtitle": "智联康复元宇宙 · 虚拟诊室构成",
        "sections": ["rooms"],
        "height": 700,
    },
    "assessment_training": {
        "filename": "data-center-report-assessment-training",
        "range": "岳阳医院评估与训练",
        "subtitle": "智联康复元宇宙 · 岳阳医院评估与训练",
        "sections": ["assessment_training"],
        "height": 750,
    },
    "records": {
        "filename": "data-center-report-records",
        "range": "业务资料",
        "subtitle": "智联康复元宇宙 · 岳阳医院业务资料",
        "sections": ["records"],
        "height": 650,
    },
    "comparison": {
        "filename": "data-center-report-comparison",
        "range": "平台与岳阳医院对比",
        "subtitle": "智联康复元宇宙 · 平台与岳阳医院对比",
        "sections": ["05"],
        "height": 700,
    },
    "analysis": {
        "filename": "data-center-report-analysis",
        "range": "AI 智能分析",
        "subtitle": "智联康复元宇宙 · 数据中心 AI 智能分析",
        "sections": ["06"],
        "height": 850,
    },
}


def section_map(source: BeautifulSoup):
    result = {}
    for section in source.select("main > section.section"):
        index = section.select_one(".section-head .index")
        if index:
            result[index.get_text(strip=True)] = section
    return result


def remove_head_note(section):
    note = section.select_one(".section-head > p")
    if note:
        note.decompose()


def make_consultation(source: BeautifulSoup, sections):
    section = copy.deepcopy(sections["02"])
    section.select_one(".section-head small").string = "CONSULTATION"
    section.select_one(".section-head h2").string = "问诊情况"
    remove_head_note(section)
    grid = section.select_one(".grid2")
    grid.clear()
    grid.append(copy.deepcopy(sections["02"].select(".grid2 > .panel")[0]))
    grid.append(copy.deepcopy(sections["04"].select(".grid2 > .panel")[0]))
    return section


def make_rooms(source: BeautifulSoup, sections):
    section = copy.deepcopy(sections["02"])
    section.select_one(".section-head small").string = "VIRTUAL ROOMS"
    section.select_one(".section-head h2").string = "虚拟诊室"
    remove_head_note(section)
    grid = section.select_one(".grid2")
    grid["class"] = list(grid.get("class", [])) + ["single-panel"]
    room_panel = copy.deepcopy(sections["02"].select(".grid2 > .panel")[1])
    grid.clear()
    grid.append(room_panel)
    return section


def make_records(source: BeautifulSoup, sections):
    section = copy.deepcopy(sections["04"])
    section.select_one(".section-head small").string = "BUSINESS RECORDS"
    section.select_one(".section-head h2").string = "业务资料"
    remove_head_note(section)
    grid = section.select_one(".grid2")
    grid["class"] = list(grid.get("class", [])) + ["single-panel"]
    record_panel = copy.deepcopy(sections["04"].select(".grid2 > .panel")[1])
    grid.clear()
    grid.append(record_panel)
    return section


def make_assessment_training(source: BeautifulSoup, sections):
    section = copy.deepcopy(sections["03"])
    section.select_one(".section-head small").string = "ASSESSMENT & TRAINING"
    section.select_one(".section-head h2").string = "岳阳医院评估与训练"
    remove_head_note(section)
    return section


def scoped_section(source: BeautifulSoup, sections, token: str):
    if token == "consultation":
        return make_consultation(source, sections)
    if token == "rooms":
        return make_rooms(source, sections)
    if token == "records":
        return make_records(source, sections)
    if token == "assessment_training":
        return make_assessment_training(source, sections)
    return copy.deepcopy(sections[token])


def append_page_css(soup: BeautifulSoup):
    style = soup.new_tag("style")
    style.string = ".grid2.single-panel{grid-template-columns:1fr}"
    soup.head.append(style)


def build_report(source_text: str, spec: dict):
    soup = BeautifulSoup(source_text, "html.parser")
    sections = section_map(soup)
    soup.title.string = f"数据中心 AI 分析报告 - {spec['range']}"
    soup.select_one(".subtitle").string = spec["subtitle"]
    meta_values = soup.select(".hero-meta b")
    meta_values[0].string = spec["range"]

    main = soup.find("main")
    main.clear()
    for token in spec["sections"]:
        main.append(scoped_section(soup, sections, token))

    append_page_css(soup)
    return "<!doctype html>\n" + str(soup)


def main():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    source_text = SOURCE.read_text(encoding="utf-8")
    for scope, spec in REPORTS.items():
        html = build_report(source_text, spec)
        path = OUTPUT_DIR / f"{spec['filename']}.html"
        path.write_text(html, encoding="utf-8", newline="\n")
        print(f"{scope}\t{spec['height']}\t{path}")


if __name__ == "__main__":
    main()
