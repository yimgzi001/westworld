"""构建并启动一个独立的西部世界试玩。"""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "experiments" / "scapi-tool-agent" / "ToolAgentGate.csproj"
WORK = ROOT / "work"


def record_version(directory, args):
    """仅记录代码和构建物的哈希，不读取凭证或环境变量的值。"""
    sources = sorted(path for base in (ROOT / "runtime", ROOT / "experiments" / "scapi-tool-agent",
                                      ROOT / "game" / "Survivalcraft" / "Westworld")
                     for path in base.rglob("*") if path.suffix in (".cs", ".csproj")
                     and not any(part in ("bin", "obj") for part in path.parts))
    sources += [Path(__file__), ROOT / "experiments" / "openai-tool-controller" / "controller.py"]
    source_hashes = {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                     for path in sources}
    binaries = {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                for path in directory.glob("*") if path.suffix == ".dll" or path.name == "Content.zip"}
    manifest = dict(createdAt=datetime.now(timezone.utc).isoformat(), seed=args.seed, actors=args.actors,
                    mode="survival" if args.survival else "smoke" if args.smoke else "terra",
                    sources=source_hashes, binaries=binaries)
    manifest["sourceSha256"] = hashlib.sha256(json.dumps(source_hashes, sort_keys=True).encode()).hexdigest()
    (directory / "实验版本.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")


def stop_game(game, directory):
    if game.poll() is not None:
        return
    (directory / "stop.txt").touch()
    try:
        game.wait(timeout=10)
    except subprocess.TimeoutExpired:
        game.kill()
        game.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--actors", type=int, choices=range(1, 5), help="同世界的 AI 人数，试玩默认 2")
    parser.add_argument("--seed", default="westworld-play", help="地图种子")
    parser.add_argument("--requests", type=int, help="指定短测请求数。普通试玩默认 12，生存长测默认不限次数")
    parser.add_argument("--watch", action="store_true", help="为生存长测打开只读跟拍，F8 显示或隐藏画面")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--smoke", action="store_true", help="使用固定响应验证游戏动作，不请求模型")
    mode.add_argument("--survival", action="store_true", help="AI 在自然世界生存，清除开局食物，默认单人")
    args = parser.parse_args()
    if args.watch and not args.survival:
        parser.error("跟拍用于生存长测，请同时指定 --survival。")
    if args.survival and args.actors not in (None, 1, 3):
        parser.error("生存验收支持单人和三人。")
    args.actors = args.actors or (1 if args.survival else 2)
    if not args.survival:
        args.requests = 12 if args.requests is None else args.requests
        if not 1 <= args.requests <= 12:
            parser.error("普通试玩的请求上限为 12。")
    if not re.fullmatch(r"[a-zA-Z0-9_-]{1,64}", args.seed):
        parser.error("种子只能包含 1 到 64 个字母、数字、连字符或下划线。")
    minimum_requests = 2 * args.actors + int(args.actors > 1) if args.smoke else args.actors
    if args.requests is not None and args.requests > 2147483647:
        parser.error("指定的短测请求数超出可表示范围。")
    if args.requests is not None and args.requests < minimum_requests:
        parser.error(f"当前人数和模式至少需要 {minimum_requests} 次请求预算。")
    if sys.platform not in ("win32", "linux"):
        parser.error("试玩支持 Windows 和 Linux 桌面。")

    portable = WORK / "dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    dotnet = str(portable) if portable.is_file() else shutil.which("dotnet")
    if not dotnet:
        parser.error("需要安装 .NET 10 SDK。")

    sys.path.insert(0, str(ROOT / "experiments" / "openai-tool-controller"))
    sys.dont_write_bytecode = True
    from controller import child_environment, file_mode, secure_key

    WORK.mkdir(exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="play-", dir=WORK))
    print(f"本场目录 {directory}", flush=True)
    build = [dotnet, "build", str(HOST), "-c", "Release", "-m:1", "-nodeReuse:false", "-o", str(directory)]
    if os.name == "nt":
        build.append("-p:WestworldWindows=true")
    game_env = child_environment()
    build_env = dict(game_env, DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="0")
    if portable.is_file():
        build_env.setdefault("DOTNET_CLI_HOME", str(WORK / "cli"))
    subprocess.run(build, cwd=ROOT, env=build_env, check=True)
    record_version(directory, args)

    key = None if args.smoke else secure_key()

    mode = "smoke" if args.smoke else "survival" if args.survival else "terra"
    command = [dotnet, str(directory / "ToolAgentGate.dll"), "play", str(args.actors),
               args.seed, str(args.requests or 0), mode]
    if args.watch:
        command.append("watch")
    # 由启动器处理 Ctrl+C，再让游戏走自己的关闭流程。
    process_group = ({"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP}
                     if os.name == "nt" else {"start_new_session": True})
    game = subprocess.Popen(command, cwd=directory, stdin=subprocess.DEVNULL, env=game_env, **process_group)
    try:
        if not args.smoke:
            file_mode(directory / "control", key, directory / "terra-usage.jsonl",
                      model="gpt-6.1-terra", reasoning_effort="none", max_requests=args.requests,
                      parallelism=2, host_stopped=lambda: game.poll() is not None,
                      survival_run=args.survival, total_seconds=None if args.survival else 650)
        return game.wait(timeout=120 if args.smoke or args.survival else None)
    finally:
        stop_game(game, directory)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130) from None
    except Exception as error:
        print(f"试玩启动中止（{type(error).__name__}）。请查看上面的构建或游戏输出。", file=sys.stderr)
        raise SystemExit(1) from None
