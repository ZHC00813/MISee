# 索引与性能设计

## 为什么分开元数据检索和内容校验

逐个计算 SHA-256 的耗时与总文件字节数相关。对于只变化少量文件的大资料库，每次比对以及更新前都读取全部内容，会让检索耗时远大于真正的局部复制。

MISee 将目录收集、内容指纹和执行拆开，提供不同检查成本的模式：快速方式避免无条件读取整库，缓存内容方式复用有效指纹，完整方式保留独立核对内容的入口。

## 缓存与通知

- 路径、大小、时间和指纹使用不区分大小写的字典索引。
- 缺失目标与子本独有文件先按大小筛选候选，再计算内容指纹；内容索引使用队列分配复用来源。
- 程序保持运行时，FileSystemWatcher 记录受影响文件和结构变更，刷新新目录子树、改名和删除。
- 缓冲溢出、监控失败、刷新中断或磁盘／根目录身份变化时，舍弃不可靠状态。
- 比对时若距离完整元数据检查超过十分钟，重新检查元数据；这不是后台定时读取全部文件内容。

普通单文件事件采用直接键查找，结构变化才处理子树前缀，避免对每条通知重复全表搜索。

## NTFS 与 exFAT

目录时间不能作为所有后代文件内容的版本号，因此不会单凭目录时间不变就跳过子树。

跨程序重开，仅在只读查询成功且 NTFS 日志编号、游标和磁盘／根目录身份相同时复用磁盘目录表。本版采用保守的“整卷日志未变”条件，没有实现完整 USN 增量重放。日志有变化、权限不足或文件系统不支持时退回元数据检查，不创建或修改卷日志。

exFAT 不能依赖 NTFS USN。重开程序或重新接盘后，通常仍需列举元数据；快速模式不会因此重新读取全部文件内容。通知可能遗漏，手动完整内容方式仍有独立用途。

## 执行与校验

执行逐项检查路径、大小、时间和新增目标占用。写入校验开启时，复制过程中顺便计算源指纹，读回实际写入内容；覆盖文件在临时文件验证后替换，原版本预先备份。移动复用在改名前核验内容。

缓存写入异常与同步工作隔离。取消复制时清理半成品，保留已完成操作。执行记录用于检查已完成结果，并非事务式自动回滚系统。

## 参考资料

本项目采用独立实现，以下资料用于了解通用设计，没有引入这些项目的源码依赖。

- [Syncthing 同步机制](https://docs.syncthing.net/users/syncing.html)：属性检查、变更通知、指纹与周期扫描。
- [Syncthing GitHub 扫描实现](https://github.com/syncthing/syncthing/blob/main/lib/scanner/walk.go)。
- [FreeFileSync 比对方式](https://freefilesync.org/manual.php?topic=comparison-settings)：时间／大小与内容比较的不同成本。
- [Microsoft Change Journals](https://learn.microsoft.com/windows/win32/fileio/change-journals)：NTFS 卷变更日志。
- [Microsoft FileSystemWatcher.Error](https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher.error)：监控异常和缓冲溢出。
- [Everything 文件夹索引](https://www.voidtools.com/support/everything/folder_indexing/)：通知与重扫的配合。
