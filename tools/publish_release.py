#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
发布 GitHub Release（不依赖 gh cli，用 git credential 里的 GCM token）

用法（项目根目录）：
    python tools/publish_release.py --version v1.6.0 \
        --exe ../fangguoge-export/fangguoge-demo-v1.6.0.exe \
        --notes notes.md [--prerelease]

流程：
  1. `git credential fill` 取 github.com 的 token
  2. POST /repos/{repo}/releases 建 Release（tag 不存在会自动建）
  3. POST uploads.github.com 上传 asset（大文件分块读）
"""
import argparse
import json
import os
import subprocess
import sys
import urllib.request

REPO = "anglelove3/fangguoge-game"


def token_from_git() -> str:
    p = subprocess.run(
        ["git", "credential", "fill"],
        input="protocol=https\nhost=github.com\n\n",
        capture_output=True, text=True, check=True,
    )
    for line in p.stdout.splitlines():
        if line.startswith("password="):
            return line.split("=", 1)[1]
    raise RuntimeError("git credential 里没有 github.com 的 password（GCM 没登录？）")


def api(url: str, token: str, method="GET", data=None, content_type="application/json"):
    req = urllib.request.Request(url, method=method)
    req.add_header("Authorization", f"token {token}")
    req.add_header("Accept", "application/vnd.github+json")
    req.add_header("User-Agent", "fangguoge-release")
    if data is not None:
        req.add_header("Content-Type", content_type)
        req.data = data
    with urllib.request.urlopen(req, timeout=300) as r:
        return json.loads(r.read().decode() or "{}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True, help="如 v1.6.0")
    ap.add_argument("--exe", required=True, help="要上传的文件路径")
    ap.add_argument("--notes", required=True, help="发布说明（markdown 文件）")
    ap.add_argument("--name", default="", help="Release 标题（默认用版本号）")
    ap.add_argument("--prerelease", action="store_true", help="标记为预发布（试玩版）")
    args = ap.parse_args()

    if not os.path.exists(args.exe):
        sys.exit(f"找不到文件：{args.exe}")

    token = token_from_git()
    body = open(args.notes, encoding="utf-8").read()
    size = os.path.getsize(args.exe)
    print(f"[1/3] token 就绪，文件 {size / 1048576:.0f} MB")

    # ---- 建 Release（已存在就复用）----
    payload = json.dumps({
        "tag_name": args.version,
        "name": args.name or args.version,
        "body": body,
        "draft": False,
        "prerelease": args.prerelease,
    }).encode()
    try:
        rel = api(f"https://api.github.com/repos/{REPO}/releases", token, "POST", payload)
        print(f"[2/3] Release 已建：{rel.get('html_url')}")
    except urllib.error.HTTPError as e:
        detail = e.read().decode()
        if e.code == 422 and "already_exists" in detail:
            rel = api(f"https://api.github.com/repos/{REPO}/releases/tags/{args.version}", token)
            print(f"[2/3] Release 已存在，复用：{rel.get('html_url')}")
        else:
            sys.exit(f"建 Release 失败 {e.code}: {detail[:300]}")

    # ---- 上传 asset ----
    name = os.path.basename(args.exe)
    url = f"https://uploads.github.com/repos/{REPO}/releases/{rel['id']}/assets?name={urllib.parse.quote(name)}"
    with open(args.exe, "rb") as f:
        data = f.read()
    print(f"[3/3] 上传 {name} ...")
    asset = api(url, token, "POST", data, "application/octet-stream")
    mb = asset.get("size", 0) / 1048576
    print(f"完成 ✓  asset={asset.get('name')}  {mb:.0f} MB")
    print(f"下载：{asset.get('browser_download_url')}")


if __name__ == "__main__":
    import urllib.parse  # noqa: E402
    import urllib.error    # noqa: E402
    main()
