import { useState } from "react";
import { createInterview, generateQuestions, getReport, submitAnswer } from "./api";
import InterviewSetup from "./components/InterviewSetup";
import QuestionPanel from "./components/QuestionPanel";
import ReportPanel from "./components/ReportPanel";

export default function App() {
  const [stage, setStage] = useState("setup");
  const [sessionId, setSessionId] = useState(null);
  const [questions, setQuestions] = useState([]);
  const [results, setResults] = useState([]);
  const [report, setReport] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  async function startInterview(formData) {
    setLoading(true); setError("");
    try {
      const interview = await createInterview(formData);
      const generated = await generateQuestions(interview.id);
      setSessionId(interview.id); setQuestions(generated.questions); setResults([]); setStage("answering");
    } catch (err) { setError(err.message); } finally { setLoading(false); }
  }

  async function answerQuestion(questionId, answerText) {
    setLoading(true); setError("");
    try {
      const evaluation = await submitAnswer(questionId, answerText);
      setResults((items) => [...items, { question: questions.find((item) => item.id === questionId), evaluation }]);
      return evaluation;
    } catch (err) { setError(err.message); return null; } finally { setLoading(false); }
  }

  async function finishInterview() {
    setLoading(true); setError("");
    try { setReport(await getReport(sessionId)); setStage("report"); }
    catch (err) { setError(err.message); } finally { setLoading(false); }
  }

  function restart() { setStage("setup"); setReport(null); setQuestions([]); setSessionId(null); setError(""); }

  return <main className="app-shell"><div className="topbar"><span>应届生模拟面试</span><span className="status-dot">● 在线练习</span></div>{error && <div className="error" role="alert">{error}</div>}{stage === "setup" && <InterviewSetup onStart={startInterview} loading={loading} />}{stage === "answering" && <QuestionPanel questions={questions} onSubmit={answerQuestion} onFinish={finishInterview} loading={loading} />}{stage === "report" && report && <ReportPanel report={report} results={results} onRestart={restart} />}</main>;
}
