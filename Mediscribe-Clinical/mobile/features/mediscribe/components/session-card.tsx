import { Pressable, View } from 'react-native';
import Ionicons from '@expo/vector-icons/Ionicons';
import { Card, CardContent } from '@/components/ui/Card';
import { HStack } from '@/components/ui/HStack';
import { VStack } from '@/components/ui/VStack';
import { Text } from '@/components/ui/Text';
import { StatusPill } from '@/features/mediscribe/components/status-pill';
import type { ClinicalSession } from '@/features/mediscribe/types';

interface SessionCardProps {
  session: ClinicalSession;
  onPress: () => void;
}

export function SessionCard({ session, onPress }: SessionCardProps) {
  const startedAt = new Date(session.startedAt).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
  });

  return (
    <Pressable onPress={onPress} className="active:opacity-80">
      <Card>
        <CardContent>
          <VStack gap="sm">
            <HStack className="justify-between">
              <VStack gap="xs" className="flex-1">
                <Text className="font-semibold text-base">{session.patient.fullName}</Text>
                <HStack gap="xs">
                  <Ionicons name="time-outline" size={16} color="#6B7280" />
                  <Text className="text-sm text-text-secondary">{startedAt}</Text>
                </HStack>
              </VStack>
              <StatusPill status={session.status} />
            </HStack>

            <View className="border-t border-border pt-3 gap-2">
              <HStack gap="xs">
                <Ionicons name="person-outline" size={16} color="#6B7280" />
                <Text className="text-sm text-text-secondary">Owner: {session.ownerName}</Text>
              </HStack>
              <HStack gap="xs">
                <Ionicons name="medical-outline" size={16} color="#6B7280" />
                <Text className="text-sm text-text-secondary">
                  {session.medicationRecommendations.length} recommendation(s)
                </Text>
              </HStack>
            </View>
          </VStack>
        </CardContent>
      </Card>
    </Pressable>
  );
}
