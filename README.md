# ClassTell

适用于希沃大屏的一个**叫人自动化命令软件**。

> **商用请联系邮箱：** [qe_ge@outlook.com](mailto:qe_ge@outlook.com)

**ClassTell 使用说明**
**版本：** `v1.0.0`（正式版 · .NET Framework 4.8 / WinForms）

---

## 一、这是什么

ClassTell 把“邮件”当作远程指令使用：

* 使用 OAuth2（Modern Auth）登录 Outlook / Microsoft 365 邮箱；
* Microsoft Graph：轮询收件箱未读邮件
* 邮件标题写命令字，正文第一行作为标题，后续内容作为正文；
* 收到“呼叫”类指令后，会弹出系统通知（通知区域气泡），并将内容显示在消息界面。


---

## 二、运行环境

* Windows 7 SP1 / 8.1 / 10 / 11

  * 建议使用 Windows 10 及以上版本，通知区域支持更加完整；
* .NET Framework 4.8 运行时

  * Windows 10 1809 及以上系统通常自带；
* 需要能够访问：

  * `outlook.office365.com:993`
  * `login.microsoftonline.com:443`

---

## 三、安装与启动

### 安装

方式一（推荐）：安装 MSI 安装包

1. 双击 `dist\ClassTell-1.0.0.msi`；
2. 向导中可勾选组件：**ClassTell 主程序（必需）** / **桌面快捷方式** / **诊断工具（可选）**；
3. 默认安装到 `C:\Program Files\ClassTell`，并创建开始菜单快捷方式；
4. 卸载：在“设置 → 应用 → 已安装的应用”内卸载，或使用开始菜单的“卸载 ClassTell”；
   卸载会自动清理程序写入的开机自启动项（`HKCU\...\Run\ClassTell`）。

方式二：绿色版（免安装）

1. 将发布目录（`dist\stage` 或编译输出 `bin\Release\net48`）整体复制到任意位置；
2. 双击 `ClassTell.exe`；
3. 首次启动会自动进入 **“关于 → 邮箱登录”**，并打开浏览器要求登录；
4. 程序为单实例运行，重复启动时会提示“已经在运行中”。

### 启动参数

以下参数为可选参数，主要用于诊断：

```text
ClassTell.exe --no-login
```

不自动弹出登录界面，可用于离线查看界面。

```text
ClassTell.exe --demo
```

注入 5 条示例消息，并模拟一次 `C / call` 通知，便于预览界面及动效。

---

## 四、软件使用
请参加"使用说明.txt"
