const statusEl = document.getElementById("status");
const messageEl = document.getElementById("message");
const eventsEl = document.getElementById("events");
const sendButton = document.getElementById("send");
const refreshButton = document.getElementById("refresh");

// 弹窗不直接访问 exe，而是把用户输入交给后台脚本，由后台脚本通过桥接页转发。
sendButton.addEventListener("click", async () => {
  const text = messageEl.value.trim();
  const result = await chrome.runtime.sendMessage({ type: "test:send", text });
  if (!result?.ok) {
    statusEl.textContent = result?.message || "发送失败";
  }
  await refresh();
});

refreshButton.addEventListener("click", refresh);

// 定时刷新状态，方便观察桥接页、本地程序授权和 exe 运行状态。
async function refresh() {
  const status = await chrome.runtime.sendMessage({ type: "test:status" });
  const events = await chrome.runtime.sendMessage({ type: "test:get-events" });

  statusEl.textContent = [
    `桥接页：${events?.bridgeConnected ? "已连接" : "未连接"}`,
    `本地程序：${status?.result?.host?.running ? "运行中" : "已停止"}`,
    `授权：${status?.result?.host?.authorized ? "已授权" : "未授权"}`
  ].join(" | ");

  eventsEl.textContent = JSON.stringify(events?.events || [], null, 2);
}

refresh();
setInterval(refresh, 1500);
