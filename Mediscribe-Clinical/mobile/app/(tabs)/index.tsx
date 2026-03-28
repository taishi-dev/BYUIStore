import { useMemo, useState } from 'react';
import { useRouter } from 'expo-router';
import Ionicons from '@expo/vector-icons/Ionicons';
import { Container } from '@/components/ui/Container';
import { VStack } from '@/components/ui/VStack';
import { HStack } from '@/components/ui/HStack';
import { Section } from '@/components/ui/Section';
import { Text } from '@/components/ui/Text';
import { Card, CardContent } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { HorizontalScroll } from '@/components/ui/HorizontalScroll';
import { Chip } from '@/components/ui/Chip';
import { SessionCard } from '@/features/mediscribe/components/session-card';
import { useMediScribe } from '@/features/mediscribe/context/mediscribe-context';
import type { SessionStatus } from '@/features/mediscribe/types';

const statusFilters: { label: string; value: SessionStatus | 'all' }[] = [
  { label: 'All', value: 'all' },
  { label: 'Pending', value: 'pending' },
  { label: 'In Review', value: 'in_review' },
  { label: 'Completed', value: 'completed' },
];

export default function DashboardScreen() {
  const router = useRouter();
  const { sessions, currentUser, signOut } = useMediScribe();
  const [query, setQuery] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<SessionStatus | 'all'>('all');
  const [sortBy, setSortBy] = useState<'time_desc' | 'time_asc' | 'patient'>('time_desc');

  const filteredSessions = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();

    const withFilter = sessions.filter((session) => {
      const byStatus = selectedStatus === 'all' ? true : session.status === selectedStatus;
      const byQuery = normalizedQuery
        ? session.patient.fullName.toLowerCase().includes(normalizedQuery)
        : true;
      return byStatus && byQuery;
    });

    return [...withFilter].sort((a, b) => {
      if (sortBy === 'patient') {
        return a.patient.fullName.localeCompare(b.patient.fullName);
      }

      const aTime = new Date(a.startedAt).getTime();
      const bTime = new Date(b.startedAt).getTime();
      return sortBy === 'time_desc' ? bTime - aTime : aTime - bTime;
    });
  }, [query, selectedStatus, sessions, sortBy]);

  const metricCounts = useMemo(() => {
    const pending = sessions.filter((session) => session.status === 'pending').length;
    const inReview = sessions.filter((session) => session.status === 'in_review').length;
    const completed = sessions.filter((session) => session.status === 'completed').length;
    return { pending, inReview, completed };
  }, [sessions]);

  return (
    <Container>
      <VStack gap="lg">
        <HStack className="justify-between">
          <VStack gap="xs" className="flex-1">
            <Text className="text-2xl font-bold">Today&apos;s Sessions</Text>
            <Text className="text-text-secondary">
              Signed in as {currentUser?.name} ({currentUser?.role})
            </Text>
          </VStack>
          <Button variant="ghost" size="sm" onPress={signOut}>
            Sign Out
          </Button>
        </HStack>

        <Card>
          <CardContent>
            <HStack className="justify-between">
              <VStack gap="xs" className="items-center">
                <Text className="text-xl font-bold">{metricCounts.pending}</Text>
                <Text className="text-sm text-text-secondary">Pending</Text>
              </VStack>
              <VStack gap="xs" className="items-center">
                <Text className="text-xl font-bold">{metricCounts.inReview}</Text>
                <Text className="text-sm text-text-secondary">In Review</Text>
              </VStack>
              <VStack gap="xs" className="items-center">
                <Text className="text-xl font-bold">{metricCounts.completed}</Text>
                <Text className="text-sm text-text-secondary">Completed</Text>
              </VStack>
            </HStack>
          </CardContent>
        </Card>

        <Section title="Queue Controls" subtitle="Search, filter, and sort active session workflows.">
          <Input
            value={query}
            onChangeText={setQuery}
            placeholder="Search by patient name"
          />

          <HorizontalScroll>
            {statusFilters.map((filter) => (
              <Chip
                key={filter.value}
                selected={selectedStatus === filter.value}
                onPress={() => setSelectedStatus(filter.value)}
              >
                {filter.label}
              </Chip>
            ))}
          </HorizontalScroll>

          <HStack gap="sm">
            <Button
              variant={sortBy === 'time_desc' ? 'primary' : 'secondary'}
              size="sm"
              onPress={() => setSortBy('time_desc')}
            >
              Newest
            </Button>
            <Button
              variant={sortBy === 'time_asc' ? 'primary' : 'secondary'}
              size="sm"
              onPress={() => setSortBy('time_asc')}
            >
              Oldest
            </Button>
            <Button
              variant={sortBy === 'patient' ? 'primary' : 'secondary'}
              size="sm"
              onPress={() => setSortBy('patient')}
            >
              Patient A-Z
            </Button>
          </HStack>
        </Section>

        <Section title="Session List" subtitle="Tap a session to open transcript, SOAP, and medication review.">
          {filteredSessions.length ? (
            <VStack gap="sm">
              {filteredSessions.map((session) => (
                <SessionCard
                  key={session.id}
                  session={session}
                  onPress={() => router.push(`/session/${session.id}`)}
                />
              ))}
            </VStack>
          ) : (
            <Card>
              <CardContent>
                <HStack>
                  <Ionicons name="search" size={18} color="#6B7280" />
                  <Text className="text-text-secondary">No sessions match your current filters.</Text>
                </HStack>
              </CardContent>
            </Card>
          )}
        </Section>
      </VStack>
    </Container>
  );
}
