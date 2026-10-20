# kvm-ddc

一台显示器给 Mac 和 Windows 两台电脑共用时，在哪台上按快捷键，画面就切到另一台。如果显示器带 KVM，键鼠也会一起切过去。

原理：通过 DDC/CI 向显示器发送 VCP `0x60`（输入源）命令，代替手动按显示器菜单。显示器内置的 KVM 会让 USB 上行口跟着输入源走，所以接在显示器上的键鼠也会跟着切到另一台。

> 这是自用代码，输入源数值和快捷键是按我的环境写死的，换环境要改，见[修改数值和快捷键](#修改数值和快捷键)。

## 我的环境

| | 连接 | 输入源值（实测） | 切到另一台的快捷键 |
|---|---|---|---|
| Mac（Apple Silicon） | 雷电 / USB-C | **25**（`0x19`） | `Ctrl+Shift+Cmd+S`（Raycast） |
| Windows | DisplayPort | **15**（`0x0F`） | `Win+Ctrl+Shift+S` |

显示器：Dell U2725QE，菜单里已开启 DDC/CI，KVM 设置为 USB 跟随输入源。

注意：这台显示器**回读的输入源值不可靠**（Windows 读到 3865，Mac 读到的是上次设置的值），不能靠回读判断当前在哪个口。上表的数值是实际切换后，看画面确认的。

## Mac

依赖 [m1ddc](https://github.com/waydabber/m1ddc)（实测版本 1.2.0）：

```bash
brew install m1ddc
```

快捷键用 [Raycast](https://www.raycast.com/) 的 Script Command 实现：

1. 把 `mac/switch-to-windows.sh` 放到一个固定目录（我的是 `~/Scripts/raycast/`），加上可执行权限
2. Raycast 设置 → Extensions → **+** → **Add Script Directory**，选择这个目录
3. 在列表中找到「显示器切到 Windows」，Hotkey 录成 `Ctrl+Shift+Cmd+S`

也可以直接在终端运行，效果一样：

```bash
m1ddc set input 15
```

限制：Mac 锁屏或登录窗口下快捷键不生效。

## Windows

C# 写的，用系统自带的 .NET Framework 4.x 编译器（`csc.exe`）编译，不需要装任何依赖。

### 安装 / 更新

用**管理员身份**运行：

```bat
windows\install.cmd
```

脚本会编译代码，把程序复制到 `C:\Program Files\ddcswitch\`，然后注册并启动 `ddcswitch` 服务（开机自动启动，异常退出后自动重启）。重复运行即可更新。

装好后，按 `Win+Ctrl+Shift+S` 切到 Mac。**正常桌面、锁屏、登录界面、UAC 弹窗下都能用。**

### 工作方式

- **服务**（`ddcswitch.exe service`）：以 SYSTEM 身份运行，开机即启动，不需要用户登录。它会把 agent 拉起到当前控制台会话里。看门狗每 60 秒检查一次，agent 退出或控制台会话变化时重新拉起。
- **agent**（`ddcswitch.exe agent`）：同时在 `Default`（正常桌面）和 `Winlogon`（登录/锁屏/UAC 安全桌面）两个桌面上挂低级键盘钩子。检测到组合键后发送 DDC 命令，并吞掉这次的 `S` 键。**只判断这一组组合键，不记录任何按键内容。**

### 日志与手动命令

日志在 `C:\Program Files\ddcswitch\ddcswitch.log`。

程序也可以手动运行。它是窗口程序，不会在控制台输出，结果写在日志里：

```bat
ddcswitch.exe list        :: 列出显示器和回读值
ddcswitch.exe set 25      :: 切到指定输入源
```

> 手动命令必须在已登录用户的桌面会话里运行。从 SSH 会话运行时，只能看到逻辑显示器，拿不到物理显示器句柄。

### 卸载

用**管理员身份**运行：

```bat
windows\uninstall.cmd
```

## 修改数值和快捷键

| 要改的 | 位置 |
|---|---|
| Windows 切到 Mac 发送的值 | `windows/src/Ddc.cs` 里的 `MAC_INPUT` |
| Windows 快捷键 | `windows/src/Agent.cs` 里的 `OnKey`（修饰键判断和 `VK_S`） |
| Mac 切到 Windows 发送的值 | `mac/switch-to-windows.sh` 最后一行 |
| Mac 快捷键 | Raycast 里重新录制 |

改完后，Windows 端重新运行 `install.cmd`。

常见输入源值（MCCS 标准）：DP1=`0x0F`(15)、DP2=`0x10`(16)、HDMI1=`0x11`(17)、HDMI2=`0x12`(18)。USB-C/雷电不在标准里，各厂商自定义（Dell 常见 `0x19` 或 `0x1B`），以实测为准。

## 已知限制

- 切换时显示器需要重新握手信号，会黑屏 1–3 秒，这是显示器本身的延迟
- 显示器菜单里需要开启 DDC/CI；部分 Mac 的内置 HDMI 口不支持 DDC，用雷电/USB-C 连接最稳妥
- Mac 锁屏和登录窗口下快捷键不可用
- Windows 上，agent 在注销或切换用户后最多需要 60 秒才会被重新拉起

## License

[MIT](LICENSE)
