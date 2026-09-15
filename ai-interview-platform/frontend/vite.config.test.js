import { describe, expect, it } from "vitest";

import viteConfig from "./vite.config.js";

describe("Vite API proxy", () => {
  it("proxies the same-origin API to the configurable backend", () => {
    const config = typeof viteConfig === "function"
      ? viteConfig({ command: "serve", mode: "test" })
      : viteConfig;

    expect(config.server.proxy["/api"].target).toBe("http://127.0.0.1:8000");
  });
});
