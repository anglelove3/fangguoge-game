#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
补传 Release 资产（可重复执行；已有同名 asset 就跳过）

用法：
    python tools/upload_asset.py --version v1.6.0 --file ../fangguoge-export/fangguoge-demo-v1.6.0.exe

背景：2026-10-22 本机网络对大文件上传反复中断（Connection reset / TLS handshake failed），
      建 Release 成功但 asset 传不上去。这个脚本用于网络恢复后补传，也可挂定时任务重试。
"""
import argparse
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

REPO = "anglelove3/fangguoge-game"


def token() -> str:
    p = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                       capture_output=True, text=True, check=True)
    for line in p.stdout.splitlines():
        if line.startswith("password="):
            return line.split("=", 1)[1]
    raise RuntimeError("GCM 里没有 github.com 的 token")


def get(url, tok, timeout=60):
    req = urllib.request.Request(url)
    req.add_header("Authorization", "token " + tok)
    req.add_header("Accept", "application/vnd.github+json")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True)
    ap.add_argument("--file", required=True)
    args = ap.parse_args()

    if not os.path.exists(args.file):
        sys.exit(f"找不到文件：{args.file}")

    tok = token()
    rel = get(f"https://api.github.com/repos/{REPO}/releases/tags/{args.version}", tok)
    name = os.path.basename(args.file)
    have = [a for a in rel.get("assets", []) if a["name"] == name]
    if have:
        print(f"✓ 已存在：{have[0]['name']}  {have[0]['size'] / 1048576:.0f} MB")
        print(f"  {have[0]['browser_download_url']}")
        return

    size = os.path.getsize(args.file)
    print(f"上传 {name}（{size / 1048576:.0f} MB）→ release {rel['id']} ...")
    url = (f"https://uploads.github.com/repos/{REPO}/releases/{rel['id']}/assets"
           f"?name={urllib.parse.quote(name)}")
    req = urllib.request.Request(url, method="POST")
    req.add_header("Authorization", "token " + tok)
    req.add_header("Content-Type", "application/octet-stream")
    with open(args.file, "rb") as f:
        req.data = f.read()
    try:
        with urllib.request.urlopen(req, timeout=1800) as r:
            a = json.loads(r.read().decode())
        print(f"✓ 完成 {a['name']}  {a['size'] / 1048576:.0f} MB")
        print(a["browser_download_url"])
    except Exception as e:
        sys.exit(f"✗ 失败：{type(e).__name__}: {e}")


if __name__ == "__main__":
    main()
