# 证据读取

`craft-dot-file06.jsonl.gz.b64.part01/part02` 拼接后为原 `craft-dot-file06.jsonl.gz.b64`；分片只为保证远程提交体积有界，不删节原始日志。

```sh
cat craft-dot-file06.jsonl.gz.b64.part* | base64 --decode | gzip -dc > craft-dot-file06.jsonl
base64 --decode craft-llama06.jsonl.gz.b64 | gzip -dc > craft-llama06.jsonl
```

用 `craft-identical-runtime06-archive-manifest.json` 中 rawSha256 验证解压输出。历史 free04/free05 证据在 archives/，依该目录说明读取。世界、素材、模型权重不上传。06试验使用旧UTC时差记录且为派发时间，不冒充修复后的单调计时或精确动作开始；详细局限见同接口验收文档。
