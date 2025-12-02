# IEC 61131-3 PLC 编辑器（.NET 8 + React + Electron）

- 前端：React + TypeScript（Vite），Monaco 用于 ST，React Flow 用于 FBD。
- 后端：.NET 8（ASP.NET Core Minimal API + SignalR），解析/IR/编译/仿真、PLCopen XML。
- 桌面：Electron 跨平台打包，启动本地后端并加载前端。

## 开发与运行

- 前端：`cd src/frontend && npm install && npm run dev`
- 后端：`dotnet build src/backend/Plc.Server && dotnet run --project src/backend/Plc.Server`
- 桌面：`cd src/shell && npm install && npm run dev`

