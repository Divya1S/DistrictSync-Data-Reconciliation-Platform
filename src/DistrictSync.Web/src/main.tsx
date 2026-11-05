import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { configureClient } from '@/api/client';
import { clearToken, getToken } from '@/auth/authStore';
import App from './App';
import './index.css';

// 401 from any endpoint drops the session; RequireAuth then redirects to /login.
configureClient({ getToken, onUnauthorized: clearToken });

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
