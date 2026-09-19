#### 编码规则

遵守基本的编码规范，例如

- Microsoft CSharp 的命名规范，文件名规范，导入规范
- python 的命名规范、文件名规范，导入规范
- typescript 的命名规范等。

#### 项目结构

- 需要拆分文件时无需犹豫，不要把大量代码挤在一个文件中。
- 每当你完成一次较大改动，在 `.codex/history` 中创建一个 markdown 文件，描述你此次改动的内容、目的、涉及到的模块、代码等。

#### 开发环境

**用以下命令进入 Visual Studio 开发环境**，在执行编译等命令。

```
# 用 powershell
$cwd = $PWD.Path; & "D:\Visual Studio\18\insiders\Common7\Tools\Launch-VsDevShell.ps1" -Arch amd64 -HostArch amd64; cd $cwd; <command to execute>

# 用 cmd
"D:\Visual Studio\18\insiders\VC\Auxiliary\Build\vcvars64.bat" && <command to execute>
```

进入开发环境后，可以使用 ninja + MSVC + cmake 构建 C++ 项目，或者 dotnet 构建 C# 项目等等。

**使用代理加速访问 github 等站点**

```
# cmd
set http_proxy=http://127.0.0.1:7890 && set https_proxy=http://127.0.0.1:7
890

# powershell
$env:http_proxy="http://127.0.0.1:7890";$env:https_proxy="http://127.0.0.1:7890"
```

