import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import App from "./App";
import { getReport } from "./api";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe("App", () => {
  it("shows the interview setup page", () => {
    render(<App />);
    expect(screen.getByText("智能面试辅助平台")).toBeInTheDocument();
    expect(screen.getByLabelText("岗位描述")).toBeInTheDocument();
  });

  it("starts an interview and displays the first question", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: "session-1" }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ questions: [{ id: "question-1", question_text: "请介绍你的项目", question_type: "项目经历", difficulty: "基础", focus_points: [], reference_direction: "" }] }), { status: 200 }));
    render(<App />);
    fireEvent.change(screen.getByLabelText("岗位描述"), { target: { value: "招聘前端开发" } });
    fireEvent.change(screen.getByLabelText("简历文件"), { target: { files: [new File(["resume"], "resume.txt", { type: "text/plain" })] } });
    fireEvent.submit(screen.getByRole("button", { name: "开始面试" }).closest("form"));
    await waitFor(() => expect(screen.getByText("请介绍你的项目")).toBeInTheDocument());
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
