import { useState } from 'react';
import { Redirect, useRouter } from 'expo-router';
import Ionicons from '@expo/vector-icons/Ionicons';
import { Container } from '@/components/ui/Container';
import { VStack } from '@/components/ui/VStack';
import { HStack } from '@/components/ui/HStack';
import { Card, CardContent } from '@/components/ui/Card';
import { Text } from '@/components/ui/Text';
import { Input } from '@/components/ui/Input';
import { Button } from '@/components/ui/Button';
import { useMediScribe, demoCredentials } from '@/features/mediscribe/context/mediscribe-context';

export default function LoginScreen() {
  const router = useRouter();
  const { currentUser, signIn, loading, authError } = useMediScribe();
  const [email, setEmail] = useState(demoCredentials.staffEmail);
  const [password, setPassword] = useState(demoCredentials.password);

  if (currentUser) {
    return <Redirect href="/(tabs)" />;
  }

  const handleSignIn = async () => {
    const ok = await signIn({ email: email.trim(), password });
    if (ok) {
      router.replace('/(tabs)');
    }
  };

  return (
    <Container keyboard>
      <VStack gap="lg" className="flex-1 justify-center pb-16">
        <VStack gap="xs">
          <HStack>
            <Ionicons name="medkit" size={26} color="#007AFF" />
            <Text className="text-2xl font-bold">MediScribe</Text>
          </HStack>
          <Text className="text-text-secondary">
            Doctor-in-control clinical documentation copilot.
          </Text>
        </VStack>

        <Card>
          <CardContent>
            <VStack gap="md">
              <VStack gap="xs">
                <Text className="font-semibold">Sign in</Text>
                <Text className="text-sm text-text-secondary">
                  Demo doctor: {demoCredentials.doctorEmail}
                </Text>
                <Text className="text-sm text-text-secondary">
                  Demo staff: {demoCredentials.staffEmail}
                </Text>
                <Text className="text-sm text-text-secondary">
                  Password: {demoCredentials.password}
                </Text>
              </VStack>

              <VStack gap="sm">
                <Input
                  value={email}
                  onChangeText={setEmail}
                  keyboardType="email-address"
                  autoCapitalize="none"
                  placeholder="Email"
                />
                <Input
                  value={password}
                  onChangeText={setPassword}
                  secureTextEntry
                  placeholder="Password"
                />
              </VStack>

              {authError ? (
                <Text className="text-sm text-error">Unable to sign in. Please verify credentials.</Text>
              ) : null}

              <Button onPress={handleSignIn} disabled={loading}>
                {loading ? 'Signing in...' : 'Sign In'}
              </Button>
            </VStack>
          </CardContent>
        </Card>
      </VStack>
    </Container>
  );
}
