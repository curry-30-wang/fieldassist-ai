import { useState } from "react";

export default function InterviewSetup({ onStart, loading }) {
  const [jobDescription, setJobDescription] = useState("");
  const [resume, setResume] = useState(null);

  function handleSubmit(event) {
    event.preventDefault();
    const formData = new FormData();
    formData.append("job_description", jobDescription.trim());
    if (resume) formData.append("resume", resume);
    onStart(formData);
  }

  return (
    <section className="card setup-card">
      <p className="eyebrow">AI INTERVIEW COACH</p>
      <h1>智能面试辅助平台</h1>
      <p className="intro">上传简历，围绕目标岗位完成一场结构化模拟面试。</p>
      <form onSubmit={handleSubmit}>
        <label htmlFor="job-description">岗位描述</label>
        <textarea
          id="job-description"
          value={jobDescription}
          onChange={(event) => setJobDescription(event.target.value)}
          placeholder="例如：负责 Python 后端接口开发、数据库设计和接口测试"
          required
          rows={6}
        />
        <label htmlFor="resume">简历文件</label>
        <input id="resume" type="file" accept=".pdf,.docx,.txt" onChange={(event) => setResume(event.target.files?.[0] || null)} required />
        {resume && <span className="file-name">已选择：{resume.name}</span>}
        <button type="submit" disabled={loading}>{loading ? "准备中…" : "开始面试"}</button>
      </form>
    </section>
  );
}
