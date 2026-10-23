import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

/**
 * Registers every prompt file in this folder. Each file exports one function whose name
 * starts with "register" and takes the server. Subfolders (such as shared/) are not scanned.
 */
export async function registerPrompts(server: McpServer) {
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);

  const promptFiles = fs
    .readdirSync(__dirname)
    .filter(
      (file) =>
        (file.endsWith(".ts") || file.endsWith(".js")) &&
        !file.endsWith(".d.ts") &&
        file !== "index.ts" &&
        file !== "index.js" &&
        file !== "register.ts" &&
        file !== "register.js"
    );

  for (const file of promptFiles) {
    try {
      const module = await import(`./${file.replace(/\.(ts|js)$/, ".js")}`);

      const registerFunctionName = Object.keys(module).find(
        (key) => key.startsWith("register") && typeof module[key] === "function"
      );

      if (registerFunctionName) {
        module[registerFunctionName](server);
        console.error(`Registered prompt: ${file}`);
      } else {
        console.warn(`Warning: no register function found in prompt file ${file}`);
      }
    } catch (error) {
      console.error(`Failed to register prompt ${file}:`, error);
    }
  }
}
