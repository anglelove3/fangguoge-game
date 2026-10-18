"""一键发试玩版（Windows）

用法（在项目根目录）：
    python tools/release.py --version v1.4.0

它会做四件事：
  1. 用 Godot 导出 Windows 版到 ../fangguoge-export/fangguoge-demo.exe
  2. 连同「说明.txt」压成 dist/fangguoge-demo-<版本>-win64.zip，并算 SHA256
  3. 用 gh 在试玩仓库发 Release（资产 = 上面那个 zip）
  4. --prune 时，只保留最近 N 个 Release（默认 2 个）

前提：gh 已登录（gh auth status），Godot 4.7.2 mono 已装。
"""

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import zipfile

GODOT = r"D:\GodotEngine\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
PRESET = "Windows Desktop"


def run(cmd, **kw):
    print("$", " ".join(cmd) if isinstance(cmd, list) else cmd)
    return subprocess.run(cmd, check=True, **kw)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True, help="如 v1.4.0")
    ap.add_argument("--repo", default="anglelove3/fangguoge-demo")
    ap.add_argument("--notes", default="", help="Release 正文（markdown 文件路径，可留空）")
    ap.add_argument("--prune", type=int, default=2, help="保留最近几个 Release，0 = 不删")
    ap.add_argument("--skip-export", action="store_true")
    args = ap.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    os.chdir(root)

    export_dir = os.path.abspath(os.path.join(root, "..", "fangguoge-export"))
    exe_path = os.path.join(export_dir, "fangguoge-demo.exe")
    readme_path = os.path.join(export_dir, "说明.txt")
    os.makedirs(export_dir, exist_ok=True)

    # 1) 导出
    if not args.skip_export:
        run([GODOT, "--headless", "--path", root, "--export-release", PRESET, exe_path])
    if not os.path.exists(exe_path):
        sys.exit(f"导出失败：没找到 {exe_path}")
    size_mb = os.path.getsize(exe_path) / (1024 * 1024)
    print(f"导出完成：{exe_path}  {size_mb:.1f} MB")
    if size_mb > 400:
        print("⚠ 包体超过 400MB，检查 export_presets.cfg 是否漏了排除 test/ 之类的大目录")

    # 2) 压包
    dist = os.path.join(root, "dist")
    os.makedirs(dist, exist_ok=True)
    zip_path = os.path.join(dist, f"fangguoge-demo-{args.version}-win64.zip")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        z.write(exe_path, "fangguoge-demo.exe")
        if os.path.exists(readme_path):
            z.write(readme_path, "说明.txt")
    digest = sha256(zip_path)
    print(f"打包完成：{zip_path}  {os.path.getsize(zip_path)/(1024*1024):.1f} MB")
    print(f"SHA256: {digest}")
    with open(zip_path + ".sha256", "w", encoding="utf-8") as f:
        f.write(f"{digest}  {os.path.basename(zip_path)}\n")

    # 3) 发 Release
    cmd = ["gh", "release", "create", args.version, zip_path,
           "--repo", args.repo,
           "--title", f"试玩版 {args.version}",
           "--notes", f"Windows 64 位试玩版。SHA256：{digest}"]
    if args.notes and os.path.exists(args.notes):
        cmd = ["gh", "release", "create", args.version, zip_path,
               "--repo", args.repo, "--title", f"试玩版 {args.version}",
               "--notes-file", args.notes]
    run(cmd)

    # 4) 清理旧版
    if args.prune > 0:
        out = subprocess.run(
            ["gh", "release", "list", "--repo", args.repo, "--limit", "50",
             "--json", "tagName", "--jq", ".[].tagName"],
            capture_output=True, text=True, check=True).stdout.split()
        keep = set(out[: args.prune])
        for tag in out[args.prune:]:
            if tag not in keep:
                print(f"删除旧 Release：{tag}")
                run(["gh", "release", "delete", tag, "--repo", args.repo,
                     "--yes", "--cleanup-tag"])

    print("完成。")


if __name__ == "__main__":
    main()
