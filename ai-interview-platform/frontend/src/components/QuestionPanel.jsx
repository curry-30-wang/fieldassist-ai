import { useState } from "react";

export default function QuestionPanel({ questions, onSubmit, onFinish, loading }) {
  const [index, setIndex] = useState(0);
  const [answer, setAnswer] = useState("");
  const [evaluation, setEvaluation] = useState(null);
  const question = questions[index];

  async function handleSubmit(event) {
    event.preventDefault();
    const result = await onSubmit(question.id, answer.trim());
    if (result) setEvaluation(result);
  }

  function next() {
    if (index === questions.length - 1) return onFinish();
    setIndex((value) => value + 1);
    setAnswer("");
    setEvaluation(null);
  }

  return (
    <section className="card question-card">
      <div className="progress">第 {index + 1} 题 / 共 {questions.length} 题</div>
      <h2>{question.question_text}</h2>
      <div className="tags"><span>{question.question_type}</span><span>{question.difficulty}</span></div>
      <form onSubmit={handleSubmit}>
        <label htmlFor="answer">你的回答</label>
        <textarea id="answer" value={answer} onChange={(event) => setAnswer(event.target.value)} rows={8} placeholder="结合具体项目、行动和结果作答" disabled={Boolean(evaluation)} required />
        {!evaluation && <button type="submit" disabled={loading}>{loading ? "评分中…" : "提交答案"}</button>}
      </form>
      {evaluation && <div className="evaluation">
        <h3>本题评分：{evaluation.score.total_score} / 10</h3>
        <p><strong>优点：</strong>{evaluation.strengths.join("、")}</p>
        <p><strong>改进建议：</strong>{evaluation.suggestions.join("、")}</p>
        <button type="button" onClick={next}>{index === questions.length - 1 ? "查看面试报告" : "下一题"}</button>
      </div>}
    </section>
  );
}
