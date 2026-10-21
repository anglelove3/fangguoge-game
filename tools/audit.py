#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
静态审计（第十六章起重建，2026-10-21）

四项：
  1. data 下全部 JSON 可解析
  2. .cs / .tscn / .json 里的 res:// 引用全部存在（排除运行时拼接的动态路径）
  3. 对白 key：代码里 PlaySequence/showChoice 引用的 key 必须在该章 dialogues json 里存在
  4. 章末卡：「—— 第X章 完 ——」的章号必须与文件名 NN 一致，且每章都要有

跑法：python tools/audit.py
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

CN = "零一二三四五六七八九十"


def cn_num(n: int) -> str:
    """1..99 转中文（够到第十七章）"""
    if n <= 10:
        return "十" if n == 10 else CN[n]
    if n < 20:
        return "十" + CN[n % 10]
    tens, ones = divmod(n, 10)
    return CN[tens] + "十" + (CN[ones] if ones else "")


problems = []

# ---------- 1. JSON ----------
json_files = []
for dirpath, _, files in os.walk("data"):
    for f in files:
        if f.endswith(".json"):
            json_files.append(os.path.join(dirpath, f))

for p in json_files:
    try:
        with open(p, encoding="utf-8") as fh:
            json.load(fh)
    except Exception as e:
        problems.append(f"[JSON] {p} 解析失败: {e}")

# ---------- 2. res:// 引用 ----------
ref_re = re.compile(r'res://[^"\')\s]+')
skip_re = re.compile(r'\{|=|%')  # 动态拼接的跳过
checked = 0
for dirpath, _, files in os.walk("."):
    if ".git" in dirpath or ".godot" in dirpath:
        continue
    for f in files:
        if not f.endswith((".cs", ".tscn", ".json", ".cfg", ".godot")):
            continue
        p = os.path.join(dirpath, f)
        try:
            text = open(p, encoding="utf-8").read()
        except Exception:
            continue
        for ref in ref_re.findall(text):
            if skip_re.search(ref) or ref.endswith("/"):
                continue
            rel = ref.replace("res://", "")
            if not os.path.exists(rel):
                problems.append(f"[REF] {p} → {ref} 不存在")
            checked += 1

# ---------- 3. 对白 key ----------
seq_re = re.compile(r'PlaySequence\(\s*ChapterId\s*,\s*\$?"([^"]+)"')
choice_re = re.compile(r'ShowChoice\(\s*ChapterId\s*,\s*"([^"]+)"')
key_checked = 0
for dirpath, _, files in os.walk("scripts"):
    for f in files:
        if not f.endswith(".cs"):
            continue
        p = os.path.join(dirpath, f)
        text = open(p, encoding="utf-8").read()
        missing = []
        for key in seq_re.findall(text) + choice_re.findall(text):
            if "{" in key:
                continue  # 动态 key 跳过
            missing.append(key)
        if not missing:
            continue
        # 找这章的 dialogues json（脚本名 ChNN.cs → data/dialogues/chNN.json）
        m = re.match(r"Ch(\d+)\.cs$", f)
        if not m:
            continue
        jp = f"data/dialogues/ch{m.group(1)}.json"
        if not os.path.exists(jp):
            problems.append(f"[KEY] {p} 找不到 {jp}")
            continue
        data = json.load(open(jp, encoding="utf-8"))
        names = set(data.get("sequences", {}).keys()) | set(data.get("choices", {}).keys()) | set(
            data.get("hearts", {}).keys())
        for k in missing:
            if k not in names:
                problems.append(f"[KEY] {p} 引用的 key「{k}」不在 {jp}")
            key_checked += 1

# ---------- 3.5 序列类型（2026-10-21 血泪：sequences 值必须是数组） ----------
for p in json_files:
    if not re.search(r"dialogues[/\\]ch\d+\.json$", p):
        continue
    d = json.load(open(p, encoding="utf-8"))
    for name, val in (d.get("sequences") or {}).items():
        if not isinstance(val, list):
            problems.append(f"[SEQ] {p} 的 sequences.{name} 不是数组（DialogueManager 会静默找不到序列）")

# ---------- 4. 章末卡 ----------
card_re = re.compile(r"—— 第([零一二三四五六七八九十]+)章 完 ——")
card_checked = 0
for dirpath, _, files in os.walk("data/dialogues"):
    for f in sorted(files):
        m = re.match(r"ch(\d+)\.json$", f)
        if not m:
            continue
        n = int(m.group(1))
        p = os.path.join(dirpath, f)
        raw = open(p, encoding="utf-8").read()
        found = card_re.findall(raw)
        if not found:
            problems.append(f"[CARD] {p} 缺章末卡")
            continue
        want = cn_num(n)
        for got in found:
            if got != want:
                problems.append(f"[CARD] {p} 章末卡写的是「第{got}章」，应为「第{want}章」")
        card_checked += 1

# ---------- 汇总 ----------
print("=" * 56)
print(f"JSON 文件：{len(json_files)} 个")
print(f"res:// 引用：{checked} 条")
print(f"对白 key：{key_checked} 处")
print(f"章末卡：{card_checked} 章")
print("=" * 56)
if problems:
    print(f"\n发现 {len(problems)} 个问题：")
    for x in problems:
        print("  " + x)
    sys.exit(1)
print("0 问题 ✓")
