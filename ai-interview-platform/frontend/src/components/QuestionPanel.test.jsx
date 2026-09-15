import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import QuestionPanel from "./QuestionPanel";

describe("QuestionPanel", () => {
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
});
