const nexusEndpoint = process.env.NEXUS_ENDPOINT || 'http://localhost:5000';
const nexusToken = process.env.NEXUS_TOKEN;

const headers = nexusToken
  ? { Authorization: `Bearer ${nexusToken}` }
  : {};

module.exports = {
  '/api': {
    target: nexusEndpoint,
    changeOrigin: true,
    secure: true,
    headers,
  },
};
