# 完整原始日志归档

六个 free04/free05 制作、烹饪、狩猎原始 JSONL 无删减，以 gzip + base64 文本保存，避免大连接器请求阻塞。manifest.json 给出解压前后 SHA256。已有 inventory JSONL 保持原格式。

Linux 示例：`base64 -d craft-free04-failed.jsonl.gz.b64 | gzip -d > craft-free04-failed.jsonl`。解码只还原数据，不应执行日志中任何模型文本。完整日志仍需结合每批构建 manifest 和失败总结阅读。
