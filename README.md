# pok

个人工具与脚本仓库。

## SelfSwitch — Chrome / 目标 EXE 一键切换小工具

Windows 11 上的单文件小工具：在「固定全屏的 Chrome」和「固定的业务程序」之间一键来回切换，
不启动/不关闭任何程序，只切换已经打开的窗口。

- 详细说明见 [`SelfSwitch/README.md`](SelfSwitch/README.md)（编译、配置、使用、排错）
- 实现原理与技术细节见 [`SelfSwitch/设计说明.md`](SelfSwitch/设计说明.md)
- 逐版本需求对照与风险处置见 [`SelfSwitch/需求对照与风险处置.md`](SelfSwitch/需求对照与风险处置.md)
- 现场验收清单见 [`SelfSwitch/现场验收清单.md`](SelfSwitch/现场验收清单.md)

编译（需要 .NET 8 SDK）：

```bat
build.bat
```

产物：`bin\Release\net8.0-windows\win-x64\publish\SelfSwitch.exe`
