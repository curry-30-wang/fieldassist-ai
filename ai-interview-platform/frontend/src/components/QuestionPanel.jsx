import { useState } from "react";

export default function QuestionPanel({ questions, onSubmit, onFinish, loading }) {
  const questionList = Array.isArray(questions) ? questions : [];
  const [index, setIndex] = useState(0);
  const [answer, setAnswer] = useState("");
  const [evaluation, setEvaluation] = useState(null);
  const [panelError, setPanelError] = useState("");
  const question = questionList[index];

  if (!questionList.length) {
    return (
      <section className="card question-card" aria-live="polite">
        <h2>暂无可用面试题</h2>
        <p className="error" role="alert">请重新生成题目后再开始答题。</p>
      </section>
    );
  }

  async function handleSubmit(event) {
    event.preventDefault();
    if (!question.id) {
      setPanelError("当前题目缺少有效编号，暂时无法提交答案。");
      return;
    }
    setPanelError("");
    const result = await onSubmit(question.id, answer.trim());
    if (result) setEvaluation(result);
  }

  function next() {
    if (index === questionList.length - 1) return onFinish();
    setIndex((value) => value + 1);
    setAnswer("");
    setEvaluation(null);
  }

  return (
    <section className="card question-card">
      <div className="progress" aria-live="polite">第 {index + 1} 题 / 共 {questionList.length} 题</div>
      <h2>{question.question_text || "题目内容暂缺"}</h2>
      <div className="tags"><span>{question.question_type || "题目类型未提供"}</span><span>{question.difficulty || "难度未提供"}</span></div>
      <form onSubmit={handleSubmit}>
        <label htmlFor="answer">你的回答</label>
        <textarea id="answer" value={answer} onChange={(event) => setAnswer(event.target.value)} rows={8} placeholder="结合具体项目、行动和结果作答" disabled={Boolean(evaluation)} required />
        {!evaluation && <button type="submit" disabled={loading} aria-busy={loading}>{loading ? "评分中…" : "提交答案"}</button>}
      </form>
      {panelError && <p className="error" role="alert">{panelError}</p>}
      {evaluation && <div className="evaluation">
        <h3>本题评分：{evaluation.score.total_score} / 10</h3>
        <p><strong>优点：</strong>{evaluation.strengths.join("、")}</p>
        <p><strong>改进建议：</strong>{evaluation.suggestions.join("、")}</p>
        <button type="button" onClick={next}>{index === questionList.length - 1 ? "查看面试报告" : "下一题"}</button>
      </div>}
    </section>
  );
}
