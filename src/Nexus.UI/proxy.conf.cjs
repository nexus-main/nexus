const nexusEndpoint = process.env.NEXUS_ENDPOINT || "http://localhost:5000";

module.exports = {
  "/api": {
    target: nexusEndpoint,
    changeOrigin: true,
    secure: true,
  },
};
