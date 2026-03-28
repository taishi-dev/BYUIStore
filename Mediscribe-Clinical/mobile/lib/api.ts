export const API_CONFIG = {
  BASE_URL: process.env.EXPO_PUBLIC_API_URL || 'http://localhost:8000',
  TIMEOUT: 10000,
} as const;

export async function mockApiDelay(duration = 250): Promise<void> {
  await new Promise((resolve) => {
    setTimeout(resolve, duration);
  });
}
