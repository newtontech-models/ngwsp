const state = {
  socket: null,
  recorder: null,
  stream: null,
  finalText: "",
  lookaheadText: "",
  isActive: false,
  log: {
    buffer: [],
    renderScheduled: false
  },
  runtime: {
    proxyUrl: null,
    sessionStartMs: null,
    lastTotalAudioMs: null
  },
  settings: {
    model: "",
    authType: "none",
    apiKey: "",
    lexiconEnabled: false,
    lexiconText: "",
    verboseLog: false
  }
};

const els = {
  model: document.getElementById("model"),
  authType: document.getElementById("authType"),
  apiKey: document.getElementById("apiKey"),
  lexiconEnabled: document.getElementById("lexiconEnabled"),
  lexiconText: document.getElementById("lexiconText"),
  lexiconErrors: document.getElementById("lexiconErrors"),
  lexiconGutter: document.getElementById("lexiconGutter"),
  importBtn: document.getElementById("importBtn"),
  exportBtn: document.getElementById("exportBtn"),
  lexiconFile: document.getElementById("lexiconFile"),
  toggleBtn: document.getElementById("toggleBtn"),
  sessionToggle: document.getElementById("sessionToggle"),
  lexiconToggle: document.getElementById("lexiconToggle"),
  sessionPanel: document.getElementById("sessionPanel"),
  lexiconWrap: document.getElementById("lexiconWrap"),
  lexiconPanel: document.getElementById("lexiconPanel"),
  verboseLog: document.getElementById("verboseLog"),
  clearLogBtn: document.getElementById("clearLogBtn"),
  status: document.getElementById("connStatus"),
  wsUrl: document.getElementById("wsUrl"),
  audioStatus: document.getElementById("audioStatus"),
  errorBox: document.getElementById("errorBox"),
  finalText: document.getElementById("finalText"),
  lookaheadText: document.getElementById("lookaheadText"),
  cursor: document.getElementById("cursor"),
  latencyLine: document.getElementById("latencyLine"),
  eventLog: document.getElementById("eventLog")
};

const STORAGE_KEY = "ngwsp.ui.v1";
const MIME_TYPE = "audio/webm; codecs=opus";
const LOG_MAX_LINES = 200;
const LOG_RENDER_THROTTLE_MS = 250;

init().catch((err) => console.warn("Init error", err));

async function init() {
  await loadConfig();
  loadSettings();
  bindEvents();
  updateUi();
  els.wsUrl.textContent = getProxyUrl();
}

async function loadConfig() {
  try {
    const response = await fetch("/ui/config.json", { cache: "no-store" });
    if (!response.ok) {
      return;
    }
    const config = await response.json();
    if (config && typeof config.ws_url === "string" && config.ws_url.length > 0) {
      state.runtime.proxyUrl = config.ws_url;
    }
  } catch (err) {
    console.warn("Failed to load UI config", err);
  }
}

function bindEvents() {
  els.model.addEventListener("input", () => saveField("model", els.model.value));
  els.authType.addEventListener("change", () => saveField("authType", els.authType.value));
  els.apiKey.addEventListener("input", () => saveField("apiKey", els.apiKey.value));
  els.lexiconEnabled.addEventListener("change", () => saveField("lexiconEnabled", els.lexiconEnabled.checked));
  els.lexiconText.addEventListener("input", () => {
    saveField("lexiconText", els.lexiconText.value);
    validateLexicon();
  });
  els.verboseLog.addEventListener("change", () => saveField("verboseLog", els.verboseLog.checked));
  els.clearLogBtn.addEventListener("click", clearLog);
  els.importBtn.addEventListener("click", () => els.lexiconFile.click());
  els.exportBtn.addEventListener("click", exportLexicon);
  els.lexiconFile.addEventListener("change", importLexicon);
  els.toggleBtn.addEventListener("click", toggleSession);
  els.sessionToggle.addEventListener("click", toggleSessionPanel);
  els.lexiconToggle.addEventListener("click", toggleLexiconPanel);
}

function loadSettings() {
  const saved = localStorage.getItem(STORAGE_KEY);
  if (!saved) {
    return;
  }
  try {
    const parsed = JSON.parse(saved);
    state.settings = { ...state.settings, ...parsed };
  } catch (err) {
    console.warn("Failed to load settings", err);
  }
}

function saveField(key, value) {
  state.settings[key] = value;
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state.settings));
}

function updateUi() {
  els.model.value = state.settings.model || "";
  els.authType.value = state.settings.authType || "none";
  els.apiKey.value = state.settings.apiKey || "";
  els.lexiconEnabled.checked = !!state.settings.lexiconEnabled;
  els.lexiconText.value = state.settings.lexiconText || "";
  els.verboseLog.checked = !!state.settings.verboseLog;
  if (!els.model.value) {
    showSessionPanel(true);
  }
  validateLexicon();
}

function setStatus(text) {
  els.status.textContent = text;
}

function setAudioStatus(text) {
  els.audioStatus.textContent = text;
}

function setError(message) {
  els.errorBox.textContent = message || "";
}

function logEvent(message) {
  const now = new Date().toISOString().split("T")[1].replace("Z", "");
  const line = `[${now}] ${message}`;
  state.log.buffer.push(line);
  if (state.log.buffer.length > LOG_MAX_LINES) {
    state.log.buffer = state.log.buffer.slice(-LOG_MAX_LINES);
  }
  scheduleLogRender();
}

function clearLog() {
  state.log.buffer = [];
  els.eventLog.textContent = "";
}

function scheduleLogRender() {
  if (state.log.renderScheduled) {
    return;
  }
  state.log.renderScheduled = true;
  setTimeout(() => {
    state.log.renderScheduled = false;
    // Render most recent first (similar to the old prepend behavior)
    const lines = [];
    for (let i = state.log.buffer.length - 1; i >= 0; i--) {
      lines.push(state.log.buffer[i]);
    }
    els.eventLog.textContent = lines.join("\n");
  }, LOG_RENDER_THROTTLE_MS);
}

function toggleSession() {
  if (state.isActive) {
    stopSession();
  } else {
    startSession();
  }
}

function toggleSessionPanel() {
  const isHidden = els.sessionPanel.classList.toggle("hidden");
  els.sessionToggle.textContent = isHidden ? "Session" : "Close Session";
}

function toggleLexiconPanel() {
  const isHidden = els.lexiconWrap.classList.toggle("hidden");
  els.lexiconToggle.textContent = isHidden ? "Lexicon" : "Close Lexicon";
}

function showSessionPanel(visible) {
  els.sessionPanel.classList.toggle("hidden", !visible);
  els.sessionToggle.textContent = visible ? "Close Session" : "Session";
}

function getProxyUrl() {
  if (state.runtime.proxyUrl) {
    return state.runtime.proxyUrl;
  }
  const protocol = window.location.protocol === "https:" ? "wss" : "ws";
  return `${protocol}://${window.location.host}/ws`;
}

async function startSession() {
  if (state.isActive) {
    return;
  }

  setError("");
  if (!state.settings.model || !state.settings.model.trim()) {
    setError("Model is required");
    showSessionPanel(true);
    return;
  }

  if (!MediaRecorder.isTypeSupported(MIME_TYPE)) {
    setError(`Browser does not support ${MIME_TYPE}`);
    return;
  }

  const authType = (state.settings.authType || "none").toLowerCase();
  if (authType === "header") {
    setError("Auth type 'header' is not supported in browser WebSocket APIs");
    return;
  }

  if (authType !== "none" && !state.settings.apiKey) {
    setError("API key is required for the selected auth type");
    return;
  }

  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    state.stream = stream;
    setAudioStatus("ready");

    const socket = await openSocket(authType);
    state.socket = socket;

    const initPayload = buildInitConfig();
    const initJson = JSON.stringify(initPayload);
    socket.send(initJson);
    logEvent(`init ${initJson}`);

    const recorder = new MediaRecorder(stream, { mimeType: MIME_TYPE });
    recorder.addEventListener("dataavailable", (event) => {
      if (event.data && event.data.size > 0 && socket.readyState === WebSocket.OPEN) {
        socket.send(event.data);
      }
    });
    recorder.addEventListener("start", () => setAudioStatus("recording"));
    recorder.addEventListener("stop", () => setAudioStatus("stopped"));

    state.runtime.sessionStartMs = Date.now();
    recorder.start(100);

    state.recorder = recorder;
    state.isActive = true;
    els.toggleBtn.textContent = "Stop";
    resetTranscript();
    logEvent("session started");
  } catch (err) {
    setError(`Failed to start: ${err.message || err}`);
    cleanupSession();
  }
}

async function stopSession() {
  if (!state.isActive) {
    return;
  }
  setError("");
  try {
    if (state.recorder && state.recorder.state !== "inactive") {
      state.recorder.stop();
    }
    if (state.socket && state.socket.readyState === WebSocket.OPEN) {
      state.socket.send(new Uint8Array());
      state.socket.close();
    }
  } catch (err) {
    console.warn("Stop error", err);
  }
  cleanupSession();
  logEvent("session stopped");
}

function cleanupSession() {
  if (state.stream) {
    state.stream.getTracks().forEach((track) => track.stop());
  }
  state.stream = null;
  state.recorder = null;
  state.socket = null;
  state.isActive = false;
  els.toggleBtn.textContent = "Start";
  setStatus("idle");
  setAudioStatus("inactive");
  state.runtime.sessionStartMs = null;
  state.runtime.lastTotalAudioMs = null;
  updateLatency(null);
}

function openSocket(authType) {
  return new Promise((resolve, reject) => {
    let url = getProxyUrl();
    const protocols = [];

    if (authType === "query" && state.settings.apiKey) {
      url = `${url}?authorization=${encodeURIComponent(state.settings.apiKey)}`;
    }

    if (authType === "subprotocol" && state.settings.apiKey) {
      protocols.push(encodeURIComponent(state.settings.apiKey));
    }

    const socket = new WebSocket(url, protocols.length ? protocols : undefined);

    socket.addEventListener("open", () => {
      setStatus("connected");
      logEvent("ws connected");
      resolve(socket);
    });
    socket.addEventListener("message", (event) => handleMessage(event));
    socket.addEventListener("close", () => {
      setStatus("closed");
      logEvent("ws closed");
    });
    socket.addEventListener("error", () => {
      setStatus("error");
      logEvent("ws error");
    });

    // Prevent hanging callers if the handshake fails.
    socket.addEventListener("close", () => {
      if (socket.readyState !== WebSocket.OPEN) {
        reject(new Error("WebSocket closed before connection opened"));
      }
    }, { once: true });
  });
}

function buildInitConfig() {
  const payload = {
    model: state.settings.model.trim()
  };

  if (state.settings.lexiconEnabled) {
    const rules = parseLexicon(state.settings.lexiconText || "");
    payload.lexicon = { rewrite_terms: rules };
  }

  return payload;
}

function parseLexicon(text) {
  const lines = text.split(/\r?\n/);
  const rules = [];
  for (const raw of lines) {
    const trimmed = raw.trim();
    if (!trimmed || trimmed.startsWith("#")) {
      continue;
    }
    const index = trimmed.indexOf(":");
    if (index <= 0) {
      continue;
    }
    const source = trimmed.slice(0, index).trim().replace(/^"|"$/g, "");
    const target = trimmed.slice(index + 1).trim().replace(/^"|"$/g, "");
    if (!source || !target) {
      continue;
    }
    rules.push({ source, target });
  }
  return rules;
}

function validateLexicon() {
  const text = els.lexiconText.value || "";
  const lines = text.split(/\r?\n/);
  const errors = [];
  const gutterLines = [];

  for (let i = 0; i < lines.length; i++) {
    const raw = lines[i];
    const trimmed = raw.trim();
    if (!trimmed || trimmed.startsWith("#")) {
      gutterLines.push("");
      continue;
    }

    const index = trimmed.indexOf(":");
    if (index <= 0) {
      errors.push(`Line ${i + 1}: expected \"source: target\"`);
      gutterLines.push("!");
      continue;
    }

    const source = trimmed.slice(0, index).trim().replace(/^\"|\"$/g, "");
    const target = trimmed.slice(index + 1).trim().replace(/^\"|\"$/g, "");
    if (!source || !target) {
      errors.push(`Line ${i + 1}: source and target must be non-empty`);
      gutterLines.push("!");
      continue;
    }

    gutterLines.push("");
  }

  els.lexiconErrors.textContent = errors.join(" | ");
  els.lexiconGutter.textContent = gutterLines.join("\n");
}

function exportLexicon() {
  const text = state.settings.lexiconText || "";
  const blob = new Blob([text], { type: "text/plain" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = "lexicon.txt";
  link.click();
  URL.revokeObjectURL(url);
}

function importLexicon(event) {
  const file = event.target.files[0];
  if (!file) {
    return;
  }
  const reader = new FileReader();
  reader.onload = () => {
    const content = reader.result ? reader.result.toString() : "";
    els.lexiconText.value = content;
    saveField("lexiconText", content);
    validateLexicon();
  };
  reader.readAsText(file);
  event.target.value = "";
}

function handleMessage(event) {
  if (!event.data) {
    return;
  }
  let payload;
  try {
    payload = JSON.parse(event.data);
    if (state.settings.verboseLog) {
      logEvent(`rx ${event.data}`);
    } else {
      const tokenCount = Array.isArray(payload.tokens) ? payload.tokens.length : 0;
      if (payload.error_code) {
        logEvent(`rx error ${payload.error_code}`);
      } else if (payload.finished) {
        logEvent("rx finished");
      } else if (tokenCount > 0) {
        const ms = typeof payload.total_audio_proc_ms === "number" ? ` total_ms=${Math.round(payload.total_audio_proc_ms)}` : "";
        logEvent(`rx transcript tokens=${tokenCount}${ms}`);
      }
    }
  } catch (err) {
    logEvent("received non-json message");
    return;
  }

  if (payload.error_code) {
    setError(`${payload.error_code}: ${payload.error_message || ""}`);
    logEvent(`error ${payload.error_code}`);
    return;
  }

  if (payload.finished) {
    els.lookaheadText.textContent = "";
    logEvent("finished");
    updateLatency(null);
    return;
  }

  if (Array.isArray(payload.tokens)) {
    const delta = applyTokens(payload.tokens);
    if (delta.final || delta.nonfinal) {
      logEvent(`text ${delta.final}|${delta.nonfinal}`);
    }
  }

  if (typeof payload.total_audio_proc_ms === "number") {
    updateLatency(payload.total_audio_proc_ms);
  }
}

function applyTokens(tokens) {
  let finalDelta = "";
  let lookaheadDelta = "";
  for (const token of tokens) {
    if (!token || typeof token.text !== "string") {
      continue;
    }
    if (token.nonspeech) {
      continue;
    }
    if (token.is_final === false) {
      lookaheadDelta += token.text;
    } else {
      finalDelta += token.text;
    }
  }

  if (finalDelta) {
    state.finalText += finalDelta;
    els.finalText.textContent = state.finalText;
  }

  if (lookaheadDelta) {
    state.lookaheadText = lookaheadDelta;
    els.lookaheadText.textContent = lookaheadDelta;
  } else {
    state.lookaheadText = "";
    els.lookaheadText.textContent = "";
  }

  return { final: finalDelta, nonfinal: lookaheadDelta };
}

function resetTranscript() {
  state.finalText = "";
  state.lookaheadText = "";
  els.finalText.textContent = "";
  els.lookaheadText.textContent = "listening...";
  updateLatency(null);
}

function updateLatency(totalAudioMs) {
  if (!state.runtime.sessionStartMs) {
    els.latencyLine.textContent = "Latency: --";
    return;
  }

  if (typeof totalAudioMs === "number") {
    state.runtime.lastTotalAudioMs = totalAudioMs;
  }

  const total = state.runtime.lastTotalAudioMs;
  if (typeof total !== "number") {
    els.latencyLine.textContent = "Latency: --";
    return;
  }

  const elapsed = Date.now() - state.runtime.sessionStartMs;
  const delta = Math.max(0, elapsed - total);
  els.latencyLine.textContent = `Latency: ${Math.round(delta)} ms`;
}
