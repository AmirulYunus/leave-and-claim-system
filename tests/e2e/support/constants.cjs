const path = require("node:path");

const root = path.resolve(__dirname, "../../..");

module.exports = {
  root,
  container: "leave-e2e-db",
  dbPort: 55433,
  appPort: 3200,
  appUrl: "http://127.0.0.1:3200",
  seedPassword: "e2e-only-password",
  databaseUrl:
    "postgresql://leave_app:e2e_only_db@127.0.0.1:55433/leave_and_claim",
  distDir: ".next-e2e",
  runDir: path.join(root, ".e2e", "run"),
  stateFile: path.join(root, ".e2e", "run", "state.json"),
  uploadDir: path.join(root, ".e2e", "uploads"),
};
