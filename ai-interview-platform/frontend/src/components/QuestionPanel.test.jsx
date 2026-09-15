import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup } from "@testing-library/react";

import QuestionPanel from "./QuestionPanel";

describe("QuestionPanel", () => {
  afterEach(cleanup);

  it("shows a stable empty state without trying to read a question", () => {
    render(<QuestionPanel questions={[]} onSubmit={vi.fn()} onFinish={vi.fn()} loading={false} />);

    expect(screen.getByText("暂无可用面试题")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("请重新生成题目");
  });

  it("falls back for missing question fields", () => {
    render(<QuestionPanel questions={[{ id: "question-1" }]} onSubmit={vi.fn()} onFinish={vi.fn()} loading={false} />);

    expect(screen.getByText("题目内容暂缺")).toBeInTheDocument();
    expect(screen.getByText("题目类型未提供")).toBeInTheDocument();
    expect(screen.getByText("难度未提供")).toBeInTheDocument();
  });

  it.each([
    ["null", null],
    ["a non-object", "not-a-question"],
    ["an object without an id", { question_text: "缺少编号" }],
  ])("disables submission and does not submit %s", (_, question) => {
    const onSubmit = vi.fn();
    const { container } = render(<QuestionPanel questions={[question]} onSubmit={onSubmit} onFinish={vi.fn()} loading={false} />);

    expect(container).toHaveTextContent("题目数据不可用，无法提交答案。");
    const submitButton = screen.getByRole("button", { name: "提交答案" });
    expect(submitButton).toBeDisabled();
    fireEvent.click(submitButton);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("submits a valid question", async () => {
    const onSubmit = vi.fn().mockResolvedValue({ score: { total_score: 8 }, strengths: [], suggestions: [] });
    render(<QuestionPanel questions={[{ id: "question-1", question_text: "请介绍项目" }]} onSubmit={onSubmit} onFinish={vi.fn()} loading={false} />);

    fireEvent.change(screen.getByLabelText("你的回答"), { target: { value: "我的项目回答" } });
    fireEvent.click(screen.getByRole("button", { name: "提交答案" }));

    expect(onSubmit).toHaveBeenCalledWith("question-1", "我的项目回答");
  });
});
