// Local identity only; the real Axios interceptor runs without initializing Firebase.
export const auth = { currentUser: { uid: 'pdf-test-user', getIdToken: async () => 'local-test-token' } };
