const nexusEndpoint = process.env.NEXUS_ENDPOINT
const nexusToken = process.env.NEXUS_TOKEN

module.exports = nexusEndpoint && nexusToken
  ? {
      '/api': {
        target: nexusEndpoint,
        changeOrigin: true,
        secure: true,
        headers: {
          Authorization: `Bearer ${nexusToken}`,
        },
      },
    }
  : {}
