const HOST_ID = "echo-host";

// 桥接页运行在 chrome-extension:// 扩展上下文中，同时又带有 Electron 预加载脚本。
// 因此它是后台脚本和主进程通信的中转层。
const port = chrome.runtime.connect({ name: "moekoe-native-host-bridge" });

// 主进程收到 exe 标准输出后会推送 native-host-message，这里再转给后台脚本。
window.electronAPI.nativeHost.onMessage((payload) => {
  if (payload?.hostId !== HOST_ID) {
    return;
  }

  port.postMessage({
    type: "native-host:event",
    payload
  });
});

// 后台脚本发来的请求在这里落到 Electron 暴露的 nativeHost API 上。
port.onMessage.addListener(async (message) => {
  if (!message || typeof message !== "object") {
    return;
  }

  // 查询本地程序当前状态：是否授权、是否运行、声明是否合法等。
  if (message.type === "native-host:status") {
    const result = await window.electronAPI.nativeHost.getStatus(HOST_ID);
    port.postMessage({
      type: "native-host:response",
      requestId: message.requestId,
      result
    });
    return;
  }

  // 发送业务消息：主进程会写入 exe 标准输入，格式为一行 JSON。
  if (message.type === "native-host:send") {
    const result = await window.electronAPI.nativeHost.send(HOST_ID, message.payload);
    port.postMessage({
      type: "native-host:response",
      requestId: message.requestId,
      result
    });
  }
});
