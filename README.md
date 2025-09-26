## AirPlay.Core2
A completely rewritten high-performance AirPlay server.  
Built with .NET 10, supporting cross-platform compatibility, standard dependency injection patterns, and improved lifecycle management for connection objects.  

[简体中文](README_zh-cn.md)

### References and Credits

#### Preface

**Special thanks to [pkillboredom](https://github.com/pkillboredom) and [Stefano Bono](https://github.com/SteeBono) for their open-source contributions to the original project [SteeBono/airplayreceiver](https://github.com/SteeBono/airplayreceiver).**

My implementation references the forked version from [pkillboredom/airplayreceiver](https://github.com/pkillboredom/airplayreceiver), and the code for audio decryption and decoding uses a modified version of the source code from that project.

> My original intention was to directly fork that project and make modifications and improvements based on it.  
> However, after carefully reviewing its source code and attempting some changes, I found that although the codebase was upgraded to .NET 8,  
> it still contained many outdated coding practices, obsolete NuGet package references, and poor type referencing and lifecycle management.  
> Additionally, the feature implementation remained incomplete and contained numerous coding errors.
> Ultimately, I decided to use it as a reference to gather relevant information and rewrite the project.

#### Reference Documents

[Unofficial AirPlay Protocol Specification](https://fingergit.github.io/Unofficial-AirPlay-Protocol-Specification/AirPlay.html)  
[AirPlay2 Internals](https://emanuelecozzi.net/docs/airplay2)  
[openairplay spec](https://openairplay.github.io/airplay-spec)  
[UxPlay Wiki](https://github.com/FDH2/UxPlay/wiki/AirPlay2)  
Some other references provided limited value and are not listed here.

> There are very few reference documents available for AirPlay, and most of the existing literature is outdated.  
> In most documents, the AirTunes service version number remains at `AirTunes/220.68`.  
> Furthermore, descriptions of protocol details in some older documents are inconsistent, making it difficult to verify their accuracy; validation through code implementation became necessary.

### Features

+ [x] Supports multiple devices connecting simultaneously to the AirPlay receiver.
+ [x] Supports audio streaming from multiple devices in ALAC and AAC formats.
+ [x] Supports packet retransmission for real-time audio streams (fixes disconnection and packet loss issues present in the original implementation).
+ [x] Supports Dacp service for reverse control of audio connections.

### Features Under Development

+ [ ] Support for screen mirroring from multiple devices.
+ [ ] Support for H.264 video stream streaming.
