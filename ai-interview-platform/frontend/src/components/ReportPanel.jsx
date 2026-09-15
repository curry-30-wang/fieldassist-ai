export default function ReportPanel({ report, results, onRestart }) {
  return <section className="card report-card">
    <p className="eyebrow">INTERVIEW REPORT</p>
    <h1>面试报告</h1>
    <div className="score"><strong>{report.total_score}</strong><span>/ 10</span></div>
    <h2>总体评价</h2><p>{report.summary}</p>
    <div className="report-grid"><div><h3>薄弱项</h3><ul>{report.weaknesses.map((item) => <li key={item}>{item}</li>)}</ul></div><div><h3>提升建议</h3><ul>{report.recommendations.map((item) => <li key={item}>{item}</li>)}</ul></div></div>
    {results.length > 0 && <><h2>逐题结果</h2><div className="result-list">{results.map((item, index) => <div className="result-item" key={item.question.id}><span>第 {index + 1} 题</span><strong>{item.evaluation.score.total_score} 分</strong><p>{item.evaluation.suggestions.join("、")}</p></div>)}</div></>}
    <button type="button" className="secondary" onClick={onRestart}>再来一场</button>
  </section>;
}
