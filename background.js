const HOST_ID = "echo-host";
const MAX_EVENTS = 30;

let bridgePort = null;
let requestId = 0;
const pending = new Map();
const events = [];

// 桥接页由主进程隐藏打开，它会主动连接后台脚本。
// 后台脚本保存这个端口，弹窗的所有请求都通过它转发给主进程。
chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== "moekoe-native-host-bridge") {
    return;
  }

  bridgePort = port;
  addEvent({ source: "后台脚本", event: "桥接页已连接", at: new Date().toISOString() });

  port.onDisconnect.addListener(() => {
    if (bridgePort === port) {
      bridgePort = null;
      addEvent({ source: "后台脚本", event: "桥接页已断开", at: new Date().toISOString() });
    }
  });

  port.onMessage.addListener((message) => {
    // exe 标准输出返回的数据会先到桥接页，再由桥接页作为事件转给后台脚本。
    if (message?.type === "native-host:event") {
      addEvent(message.payload);
      return;
    }

    // 弹窗发出的请求需要异步等待桥接页返回结果，这里用 requestId 做一次性匹配。
    if (message?.type === "native-host:response") {
      const resolver = pending.get(message.requestId);
      if (resolver) {
        pending.delete(message.requestId);
        resolver(message.result);
      }
    }
  });
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || typeof message !== "object") {
    sendResponse({ ok: false, message: "消息格式不正确" });
    return;
  }

  // 弹窗读取最近收到的本地程序事件。
  if (message.type === "test:get-events") {
    sendResponse({ ok: true, events, bridgeConnected: Boolean(bridgePort) });
    return;
  }

  // 弹窗查询授权、运行状态。
  if (message.type === "test:status") {
    sendBridgeRequest("native-host:status", {})
      .then((result) => sendResponse({ ok: true, result, bridgeConnected: Boolean(bridgePort) }))
      .catch((error) => sendResponse({ ok: false, message: error.message }));
    return true;
  }

  // 弹窗发送测试文本。后台脚本不解析业务内容，只负责转发。
  if (message.type === "test:send") {
    sendBridgeRequest("native-host:send", {
      text: String(message.text || ""),
      sentAt: new Date().toISOString()
    })
      .then((result) => sendResponse({ ok: true, result }))
      .catch((error) => sendResponse({ ok: false, message: error.message }));
    return true;
  }

  sendResponse({ ok: false, message: "未知消息类型" });
});

function sendBridgeRequest(type, payload) {
  if (!bridgePort) {
    return Promise.reject(new Error("桥接页尚未连接，请先在插件管理页授权本地程序。"));
  }

  // requestId 用来把桥接页的异步响应关联回弹窗的这次请求。
  const id = ++requestId;
  bridgePort.postMessage({
    type,
    hostId: HOST_ID,
    payload,
    requestId: id
  });

  return new Promise((resolve) => {
    pending.set(id, resolve);
  });
}

function addEvent(event) {
  // 只保留最近的事件，避免测试弹窗里堆积过多日志。
  events.unshift({
    ...event,
    receivedAt: new Date().toISOString()
  });

  if (events.length > MAX_EVENTS) {
    events.length = MAX_EVENTS;
  }
}
