import { Redirect } from 'expo-router';

export default function AuthRoute() {
  return <Redirect href="/auth/login" />;
}
