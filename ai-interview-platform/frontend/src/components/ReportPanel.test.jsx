import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import ReportPanel from "./ReportPanel";

describe("ReportPanel", () => {
  it("shows component scores and detailed feedback from answer evaluations", () => {
    render(
      <ReportPanel
        report={{
          total_score: 8,
          summary: "整体不错",
          weaknesses: ["项目细节"],
          recommendations: ["补充结果"],
          results: [{
            question: { id: "question-1", question_text: "请介绍项目" },
            answer_text: "我的项目回答",
            evaluation: {
              score: { total_score: 8, accuracy: 9, completeness: 7, relevance: 8, clarity: 8 },
              strengths: ["结构清楚"],
              problems: ["缺少数据"],
              suggestions: ["补充量化结果"],
              answer_structure: "背景、行动、结果",
            },
          }],
        }}
        onRestart={vi.fn()}
      />,
    );

    expect(screen.getByText("准确性：9 / 10")).toBeInTheDocument();
    expect(screen.getByText("完整性：7 / 10")).toBeInTheDocument();
    expect(screen.getByText("相关性：8 / 10")).toBeInTheDocument();
    expect(screen.getByText("表达清晰度：8 / 10")).toBeInTheDocument();
    expect(screen.getByText("结构清楚")).toBeInTheDocument();
    expect(screen.getByText("缺少数据")).toBeInTheDocument();
    expect(screen.getByText("背景、行动、结果")).toBeInTheDocument();
    expect(screen.getByText("我的项目回答")).toBeInTheDocument();
  });
});
