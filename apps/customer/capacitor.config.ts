import type { CapacitorConfig } from "@capacitor/cli";

// Android/iOS wrapper. First time:  npm run build && npx cap add android && npx cap open android
const config: CapacitorConfig = {
  appId: "za.co.metrorail.app",
  appName: "MetroRail",
  webDir: "dist",
  server: { androidScheme: "https" },
  plugins: {
    // Screenshots/recording are blocked only while a ticket is on screen (see ShowTicket).
    PrivacyScreen: { enable: false },
  },
};

export default config;
