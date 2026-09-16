import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import App from "./App";
import { getReport } from "./api";

const response = (payload, status = 200) => new Response(JSON.stringify(payload), { status });

const reportPayload = {
  total_score: 8,
  summary: "历史报告已恢复",
  weaknesses: ["项目细节"],
  recommendations: ["补充结果"],
  results: [{
    question: { id: "question-1", question_text: "请介绍你的项目" },
    answer_text: "我的项目回答",
    evaluation: {
      score: { total_score: 8, accuracy: 9, completeness: 7, relevance: 8, clarity: 8 },
      strengths: ["结构清楚"],
      problems: ["缺少数据"],
      suggestions: ["补充结果"],
      answer_structure: "背景、行动、结果",
    },
  }],
};

afterEach(() => { cleanup(); vi.restoreAllMocks(); localStorage.clear(); });

describe("App", () => {
  it("shows the interview setup page and loads history", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(response({ interviews: [] }));
    render(<App />);
    expect(screen.getByText("智能面试辅助平台")).toBeInTheDocument();
    expect(screen.getByLabelText("岗位描述")).toBeInTheDocument();
    await waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith("/api/interviews", {}));
  });

  it("starts an interview and displays the first question", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(response({ interviews: [] }))
      .mockResolvedValueOnce(response({ id: "session-1" }, 201))
      .mockResolvedValueOnce(response({ questions: [{ id: "question-1", question_text: "请介绍你的项目", question_type: "项目经历", difficulty: "基础", focus_points: [], reference_direction: "" }] }));
    render(<App />);
    await waitFor(() => expect(screen.getByText("暂无历史面试")).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText("岗位描述"), { target: { value: "招聘前端开发" } });
    fireEvent.change(screen.getByLabelText("简历文件"), { target: { files: [new File(["resume"], "resume.txt", { type: "text/plain" })] } });
    fireEvent.submit(screen.getByRole("button", { name: "开始面试" }).closest("form"));
    await waitFor(() => expect(screen.getByText("请介绍你的项目")).toBeInTheDocument());
  });

  it("opens a historical report using backend data", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(response({ interviews: [{ id: "session-1", job_title: "后端工程师", status: "completed", created_at: "2026-09-16T00:00:00" }] }))
      .mockResolvedValueOnce(response(reportPayload));

    render(<App />);

    await waitFor(() => expect(screen.getByText("后端工程师")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "查看报告" }));
    await waitFor(() => expect(screen.getByText("历史报告已恢复")).toBeInTheDocument());
    expect(screen.getByText("准确性：9 / 10")).toBeInTheDocument();
    expect(screen.getByText("我的项目回答")).toBeInTheDocument();
  });

  it("restores the last report after a page refresh", async () => {
    localStorage.setItem("lastInterviewSessionId", "session-1");
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(response({ interviews: [] }))
      .mockResolvedValueOnce(response(reportPayload));

    render(<App />);

    await waitFor(() => expect(screen.getByText("历史报告已恢复")).toBeInTheDocument());
    expect(screen.getByText("第 1 题：请介绍你的项目")).toBeInTheDocument();
  });

  it("completes a new interview and renders the persisted report results", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(response({ interviews: [] }))
      .mockResolvedValueOnce(response({ id: "session-1" }, 201))
      .mockResolvedValueOnce(response({ questions: [{ id: "question-1", question_text: "请介绍你的项目", question_type: "项目经历", difficulty: "基础", focus_points: [], reference_direction: "" }] }))
      .mockResolvedValueOnce(response(reportPayload.results[0].evaluation, 201))
      .mockResolvedValueOnce(response(reportPayload));

    render(<App />);
    await waitFor(() => expect(screen.getByText("暂无历史面试")).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText("岗位描述"), { target: { value: "招聘前端开发" } });
    fireEvent.change(screen.getByLabelText("简历文件"), { target: { files: [new File(["resume"], "resume.txt", { type: "text/plain" })] } });
    fireEvent.submit(screen.getByRole("button", { name: "开始面试" }).closest("form"));
    await waitFor(() => expect(screen.getByText("请介绍你的项目")).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText("你的回答"), { target: { value: "我的项目回答" } });
    fireEvent.click(screen.getByRole("button", { name: "提交答案" }));
    await waitFor(() => expect(screen.getByText("准确性：9 / 10")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "查看面试报告" }));
    await waitFor(() => expect(screen.getByText("历史报告已恢复")).toBeInTheDocument());
    expect(screen.getByText("我的项目回答")).toBeInTheDocument();
  });

  it("throws the backend detail for a non-2xx response", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify({ detail: "会话不存在" }), { status: 404 }));
    await expect(getReport("missing")).rejects.toThrow("会话不存在");
    expect(globalThis.fetch).toHaveBeenCalledWith("/api/interviews/missing/report", {});
  });

  it("uses a useful fallback when a non-2xx response has no detail", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("not json", { status: 503 }));
    await expect(getReport("missing")).rejects.toThrow("请求失败（503）");
  });
});
