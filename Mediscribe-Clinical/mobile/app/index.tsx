import { Redirect } from 'expo-router';
import { useMediScribe } from '@/features/mediscribe/context/mediscribe-context';

export default function Index() {
  const { currentUser } = useMediScribe();

  if (!currentUser) {
    return <Redirect href="/auth/login" />;
  }

  return <Redirect href="/(tabs)" />;
}
