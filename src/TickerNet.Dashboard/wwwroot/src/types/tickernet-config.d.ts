declare global {
  interface Window {
    TickerNetConfig?: {
      basePath: string;
      backendDomain?: string;
      auth: {
        mode: 'none' | 'basic' | 'apikey' | 'host' | 'custom';
        enabled: boolean;
        sessionTimeout: number;
      };
    };
  }
}

export {};