# SSH 测试夹具

这套夹具用于真实 SSH 认证、Local / Remote 转发、重连和故障测试，不参与应用运行。它需要 Windows 上的 PowerShell 7，以及 WSL Ubuntu 中可用的 Docker。

| 文件 | 用途 |
| --- | --- |
| `Start-SshFixture.ps1` | 构建镜像，创建本次独立容器，等待服务就绪并输出 manifest 路径。 |
| `Stop-SshFixture.ps1` | 校验 manifest 和容器标签，停止对应容器并清理临时凭据。 |
| `sshd/Dockerfile` | 固定 Ubuntu / OpenSSH 版本，并打包 Python 控制服务。 |
| `sshd/fixture_server.py` | 容器内的 sshd、echo 服务、就绪检查和故障控制入口。 |

通常从仓库根目录运行：

```powershell
./scripts/Test.ps1 -Ssh

# 已构建对应夹具镜像时可复用
./scripts/Test.ps1 -Ssh -NoFixtureBuild
```

按需单独启动和停止：

```powershell
$fixture = ./scripts/fixtures/Start-SshFixture.ps1
try {
    # Run the checks that need this fixture.
}
finally {
    ./scripts/fixtures/Stop-SshFixture.ps1 -ManifestPath $fixture
}
```

启动脚本默认使用 `Ubuntu`，可通过 `-Distribution` 指定 WSL 发行版。可用 `-NoBuild` 复用现有镜像。SSH 端口仅发布到本机 loopback；manifest 和临时凭据写入仓库的 `.test-output/`，停止后仅保留诊断日志。详细行为见 [开发基线](../../docs/baseline.md#ssh-夹具)。

这些入口原来位于 `test-infra/`；当前调用方和文档已更新，历史 evidence / history 中的旧路径保持原样。
