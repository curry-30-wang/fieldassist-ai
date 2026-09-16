export default function ReportPanel({ report, onRestart }) {
  const safeReport = report || {};
  const safeResults = Array.isArray(safeReport.results) ? safeReport.results : [];
  const list = (items) => Array.isArray(items) && items.length ? items.join("、") : "暂无";

  return <section className="card report-card">
    <p className="eyebrow">INTERVIEW REPORT</p>
    <h1>面试报告</h1>
    <div className="score"><strong>{safeReport.total_score ?? "暂无"}</strong><span>/ 10</span></div>
    <h2>总体评价</h2><p>{safeReport.summary || "暂无总体评价"}</p>
    <div className="report-grid"><div><h3>薄弱项</h3><ul>{(safeReport.weaknesses || []).map((item) => <li key={item}>{item}</li>)}</ul></div><div><h3>提升建议</h3><ul>{(safeReport.recommendations || []).map((item) => <li key={item}>{item}</li>)}</ul></div></div>
    {safeResults.length > 0 && <><h2>逐题结果</h2><div className="result-list">{safeResults.map((item, index) => {
      const evaluation = item?.evaluation || {};
      const score = evaluation.score || {};
      return <div className="result-item" key={item?.question?.id || index}>
        <span>第 {index + 1} 题：{item?.question?.question_text || "题目内容暂缺"}</span>
        <strong>{score.total_score ?? "暂无"} 分</strong>
        <div className="component-scores" aria-label={`第 ${index + 1} 题分项得分`}>
          <span>准确性：{score.accuracy ?? "暂无"} / 10</span>
          <span>完整性：{score.completeness ?? "暂无"} / 10</span>
          <span>相关性：{score.relevance ?? "暂无"} / 10</span>
          <span>表达清晰度：{score.clarity ?? "暂无"} / 10</span>
        </div>
        <p><strong>你的回答：</strong>{item?.answer_text || "暂无"}</p>
        <p><strong>优点：</strong>{list(evaluation.strengths)}</p>
        <p><strong>问题：</strong>{list(evaluation.problems)}</p>
        <p><strong>改进建议：</strong>{list(evaluation.suggestions)}</p>
        <p><strong>参考回答结构：</strong>{evaluation.answer_structure || "暂无"}</p>
      </div>;
    })}</div></>}
    <button type="button" className="secondary" onClick={onRestart}>再来一场</button>
  </section>;
}
