## AirPlay.Core2
一个全新重写的高性能 AirPlay 服务端  
使用 dotnet 10 编译，支持跨平台、标准的依赖注入模式，更好的连接对象生命周期管理。

### 参考和引用

#### 前言

**感谢 [pkillboredom](https://github.com/pkillboredom) 和 [Stefano Bono](https://github.com/SteeBono) 在原项目 [SteeBono/airplayreceiver](https://github.com/SteeBono/airplayreceiver) 上的开源贡献**  

我的代码实现参考了 [pkillboredom/airplayreceiver](https://github.com/pkillboredom/airplayreceiver) 分支后的版本，音频解密和解码部分代码使用了其项目源代码修改后的版本

> 我的本意是想直接 fork 该项目，并在此基础上修改和维护。  
> 但是在我仔细阅读其源代码并尝试做出一些修改后，发现尽管其源代码已经升级到 dotnet 8，  
> 但仍然存在很多的过时代码写法以及过时 Nuget 引用，还有糟糕的类型引用和生命周期管理。  
> 同时功能实现上仍然不完整，并存在许多的编写错误。
> 最终我决定以此为参考收集相关资料，对该项目进行重写。

#### 参考文献

[AirPlay 协议规范（非官方）](https://fingergit.github.io/Unofficial-AirPlay-Protocol-Specification/AirPlay.html)  
[AirPlay2 Internals](https://emanuelecozzi.net/docs/airplay2)  
[openairplay spec](https://openairplay.github.io/airplay-spec)  
[UxPlay Wiki](https://github.com/FDH2/UxPlay/wiki/AirPlay2)  
还有部分参考文献参考意义不大，此处不列出

> AirPlay 的相关参考文献少之又少、且大多数文献过于陈旧  
> 大部分文献中 AirTunes 的服务版本号都停留在 `AirTunes/220.68`  
> 并且部分旧文献中对于协议细节的表述不一致，已无从考证，只能通过代码验证

### 功能

+ [x] 支持多个设备同时连接到 AirPlay 接收端
+ [x] 支持多设备 ALAC、AAC、AAC-ELD 格式的音频流投送
+ [x] 支持多设备 H.264 格式的屏幕镜像流投送
+ [x] 支持实时音频传输的丢包重传 (修复了源实现中断连和丢包的错误)
+ [x] 支持 Dacp 服务来反向控制音频连接

### 正在实现的功能

+ [ ] 支持视频流投送
