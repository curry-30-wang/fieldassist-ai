import { useEffect, useState } from "react";
import { createInterview, generateQuestions, getReport, listInterviews, submitAnswer } from "./api";
import InterviewSetup from "./components/InterviewSetup";
import QuestionPanel from "./components/QuestionPanel";
import ReportPanel from "./components/ReportPanel";

export default function App() {
  const [stage, setStage] = useState("setup");
  const [sessionId, setSessionId] = useState(null);
  const [questions, setQuestions] = useState([]);
  const [report, setReport] = useState(null);
  const [history, setHistory] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;

    async function restore() {
      try {
        const payload = await listInterviews();
        if (active) setHistory(Array.isArray(payload?.interviews) ? payload.interviews : []);
      } catch (err) {
        if (active) setError(err.message);
      } finally {
        if (active) setHistoryLoading(false);
      }

      const savedSessionId = localStorage.getItem("lastInterviewSessionId");
      if (!savedSessionId || !active) return;
      try {
        const savedReport = await getReport(savedSessionId);
        if (active) {
          setSessionId(savedSessionId);
          setReport(savedReport);
          setStage("report");
          setError("");
        }
      } catch {
        localStorage.removeItem("lastInterviewSessionId");
      }
    }

    restore();
    return () => { active = false; };
  }, []);

  async function startInterview(formData) {
    setLoading(true); setError("");
    try {
      const interview = await createInterview(formData);
      const generated = await generateQuestions(interview.id);
      setSessionId(interview.id); setQuestions(generated.questions); setStage("answering");
    } catch (err) { setError(err.message); } finally { setLoading(false); }
  }

  async function answerQuestion(questionId, answerText) {
    setLoading(true); setError("");
    try {
      const evaluation = await submitAnswer(questionId, answerText);
      return evaluation;
    } catch (err) { setError(err.message); return null; } finally { setLoading(false); }
  }

  async function finishInterview() {
    setLoading(true); setError("");
    try {
      const savedReport = await getReport(sessionId);
      setReport(savedReport);
      localStorage.setItem("lastInterviewSessionId", sessionId);
      setStage("report");
    }
    catch (err) { setError(err.message); } finally { setLoading(false); }
  }

  async function openHistoricalReport(historicalSessionId) {
    setLoading(true); setError("");
    try {
      const savedReport = await getReport(historicalSessionId);
      setSessionId(historicalSessionId);
      setReport(savedReport);
      localStorage.setItem("lastInterviewSessionId", historicalSessionId);
      setStage("report");
    } catch (err) { setError(err.message); } finally { setLoading(false); }
  }

  function restart() {
    localStorage.removeItem("lastInterviewSessionId");
    setStage("setup"); setReport(null); setQuestions([]); setSessionId(null); setError("");
  }

  return <main className="app-shell"><div className="topbar"><span>应届生模拟面试</span><span className="status-dot">● 在线练习</span></div>{loading && <div role="status" aria-live="polite">正在处理，请稍候…</div>}{error && <div className="error" role="alert">{error}</div>}{stage === "setup" && <InterviewSetup onStart={startInterview} loading={loading} history={history} historyLoading={historyLoading} onOpenHistory={openHistoricalReport} />}{stage === "answering" && <QuestionPanel questions={questions} onSubmit={answerQuestion} onFinish={finishInterview} loading={loading} />}{stage === "report" && report && <ReportPanel report={report} onRestart={restart} />}</main>;
}
