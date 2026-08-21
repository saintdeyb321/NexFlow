import { initializeApp } from 'firebase/app';
import { getAuth } from 'firebase/auth';

// Si Vite no lee el .env, esto lanzará un error claro en lugar de usar claves falsas
if (!import.meta.env.VITE_FIREBASE_API_KEY) {
  throw new Error("Faltan las variables de entorno de Firebase. Revisa tu archivo .env");
}

const firebaseConfig = {
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
  authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN,
  projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID,
  storageBucket: import.meta.env.VITE_FIREBASE_STORAGE_BUCKET,
  messagingSenderId: import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID,
  appId: import.meta.env.VITE_FIREBASE_APP_ID
};

const app = initializeApp(firebaseConfig);
export const auth = getAuth(app);