const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL || "/api";
const baseUrl = configuredBaseUrl.replace(/\/+$/, "");

async function request(path, options = {}) {
  const response = await fetch(`${baseUrl}/${path.replace(/^\/+/, "")}`, options);
  let payload = null;
  try {
    payload = await response.json();
  } catch {
    payload = null;
  }
  if (!response.ok) {
    throw new Error(payload?.detail || `请求失败（${response.status}）`);
  }
  return payload;
}

export function createInterview(formData) {
  return request("interviews", { method: "POST", body: formData });
}

export function generateQuestions(sessionId) {
  return request(`interviews/${encodeURIComponent(sessionId)}/questions`, { method: "POST" });
}

export function submitAnswer(questionId, answerText) {
  return request(`questions/${encodeURIComponent(questionId)}/answers`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ answer_text: answerText }),
  });
}

export function getReport(sessionId) {
  return request(`interviews/${encodeURIComponent(sessionId)}/report`);
}

export function listInterviews() {
  return request("interviews");
}
