// main.tsx — where the interface starts.
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { AppProvider } from './app/AppState';
import { applyStartupView } from './app/router';
import { ToastProvider } from './components/Toast';
import './styles/app.css';

// before the first render, so a screen asked for on the command line does not flash the start screen first
applyStartupView();

const root = document.getElementById('root');
if (!root) throw new Error('index.html has no #root');

createRoot(root).render(
  <StrictMode>
    <AppProvider>
      <ToastProvider>
        <App />
      </ToastProvider>
    </AppProvider>
  </StrictMode>,
);
