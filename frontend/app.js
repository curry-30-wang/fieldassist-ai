const { createApp } = Vue;

createApp({
  data() {
    return {
      title: "FieldAssist",
      message: "The application skeleton is ready.",
      status: "Checking service status...",
      healthy: false,
    };
  },
  async mounted() {
    try {
      const response = await fetch("/api/health");
      const payload = await response.json();
      this.healthy = response.ok && payload.success && payload.data.status === "ok";
      this.status = this.healthy ? "Service online" : "Service unavailable";
    } catch (_error) {
      this.status = "Service unavailable";
    }
  },
}).mount("#app");
