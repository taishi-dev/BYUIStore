import { useMemo, useState } from 'react';
import { Pressable, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import Ionicons from '@expo/vector-icons/Ionicons';
import { Container } from '@/components/ui/Container';
import { VStack } from '@/components/ui/VStack';
import { HStack } from '@/components/ui/HStack';
import { Section } from '@/components/ui/Section';
import { Text } from '@/components/ui/Text';
import { Card, CardContent } from '@/components/ui/Card';
import { Input } from '@/components/ui/Input';
import { Button } from '@/components/ui/Button';
import { HorizontalScroll } from '@/components/ui/HorizontalScroll';
import { StatusPill } from '@/features/mediscribe/components/status-pill';
import { useMediScribe } from '@/features/mediscribe/context/mediscribe-context';
import type { ClinicalSession, MedicationRecommendationStatus } from '@/features/mediscribe/types';

const soapSections: {
  key: 'subjective' | 'objective' | 'assessment' | 'plan';
  title: string;
}[] = [
  { key: 'subjective', title: 'Subjective' },
  { key: 'objective', title: 'Objective' },
  { key: 'assessment', title: 'Assessment' },
  { key: 'plan', title: 'Plan' },
];

const recommendationStates: MedicationRecommendationStatus[] = [
  'suggested',
  'reviewed',
  'approved',
  'rejected',
];

export default function SessionDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id?: string | string[] }>();
  const sessionId = typeof params.id === 'string' ? params.id : params.id?.[0];

  const {
    sessions,
    currentUser,
    updateSessionStatus,
    updateTranscriptText,
    updateSoapSection,
    updateRecommendationStatus,
    toggleDoctorValidation,
    approveAndSaveSession,
    regeneratePostVisitSummary,
    updatePostVisitSummary,
  } = useMediScribe();

  const [expandedSections, setExpandedSections] = useState<Record<string, boolean>>({
    subjective: true,
    objective: true,
    assessment: true,
    plan: true,
  });
  const [resultMessage, setResultMessage] = useState<string | null>(null);

  const session = useMemo<ClinicalSession | undefined>(
    () => sessions.find((item) => item.id === sessionId),
    [sessionId, sessions],
  );

  if (!session) {
    return (
      <Container>
        <VStack gap="lg">
          <Text className="text-xl font-semibold">Session not found</Text>
          <Text className="text-text-secondary">
            The requested consultation may have been removed or not synced yet.
          </Text>
          <Button onPress={() => router.replace('/(tabs)')}>Back to Dashboard</Button>
        </VStack>
      </Container>
    );
  }

  const handleBack = () => {
    if (router.canGoBack()) {
      router.back();
      return;
    }
    router.replace('/(tabs)');
  };

  const isDoctor = currentUser?.role === 'doctor';

  const allRecommendationsValidated = session.medicationRecommendations.every(
    (recommendation) => !recommendation.requiresDoctorValidation || recommendation.validatedByDoctor,
  );

  const onApprove = () => {
    const result = approveAndSaveSession(session.id);
    if (!result.ok) {
      setResultMessage(result.reason ?? 'Unable to approve this session right now.');
      return;
    }
    setResultMessage('Session approved and marked as Completed.');
  };

  return (
    <Container keyboard edges={['top', 'bottom']}>
      <VStack gap="lg">
        <HStack className="justify-between">
          <Button variant="ghost" size="sm" onPress={handleBack}>
            Back
          </Button>
          <StatusPill status={session.status} />
        </HStack>

        <Card>
          <CardContent>
            <VStack gap="sm">
              <Text className="text-xl font-semibold">{session.patient.fullName}</Text>
              <Text className="text-text-secondary">
                {session.patient.age} years • {session.patient.sex}
              </Text>
              <Text className="text-sm text-text-secondary">Owner: {session.ownerName}</Text>
              <Text className="text-sm text-text-secondary">
                Allergies: {session.patient.allergies.join(', ') || 'None reported'}
              </Text>
              <Text className="text-sm text-text-secondary">
                Past ineffective medications:{' '}
                {session.patient.ineffectiveMedications.join(', ') || 'None listed'}
              </Text>
            </VStack>
          </CardContent>
        </Card>

        <Section title="Workflow State" subtitle="Staff can prepare drafts. Doctor retains final authority.">
          <HorizontalScroll>
            <Button
              size="sm"
              variant={session.status === 'pending' ? 'primary' : 'secondary'}
              onPress={() => updateSessionStatus(session.id, 'pending')}
            >
              Pending
            </Button>
            <Button
              size="sm"
              variant={session.status === 'in_review' ? 'primary' : 'secondary'}
              onPress={() => updateSessionStatus(session.id, 'in_review')}
            >
              In Review
            </Button>
            <Button
              size="sm"
              variant={session.status === 'completed' ? 'primary' : 'secondary'}
              disabled={!isDoctor}
              onPress={() => updateSessionStatus(session.id, 'completed')}
            >
              Completed
            </Button>
          </HorizontalScroll>
        </Section>

        <Section title="Transcript" subtitle="Editable doctor/patient transcript with confidence context.">
          <VStack gap="sm">
            {session.transcript.map((entry) => (
              <Card key={entry.id}>
                <CardContent>
                  <VStack gap="sm">
                    <HStack className="justify-between">
                      <HStack>
                        <Ionicons
                          name={entry.speaker === 'doctor' ? 'medkit-outline' : 'person-outline'}
                          size={16}
                          color="#007AFF"
                        />
                        <Text className="font-semibold capitalize">{entry.speaker}</Text>
                      </HStack>
                      <Text className="text-xs text-text-secondary">
                        Confidence: {Math.round(entry.confidence * 100)}%
                      </Text>
                    </HStack>
                    <Input
                      value={entry.text}
                      onChangeText={(value) => updateTranscriptText(session.id, entry.id, value)}
                      multiline
                      numberOfLines={4}
                      className="min-h-24"
                      textAlignVertical="top"
                    />
                  </VStack>
                </CardContent>
              </Card>
            ))}
          </VStack>
        </Section>

        <Section title="SOAP Composer" subtitle="Editable collapsible sections. Doctor approval required before save.">
          <VStack gap="sm">
            {soapSections.map((section) => {
              const expanded = expandedSections[section.key];
              return (
                <Card key={section.key}>
                  <CardContent>
                    <VStack gap="sm">
                      <Pressable
                        onPress={() =>
                          setExpandedSections((previous) => ({
                            ...previous,
                            [section.key]: !previous[section.key],
                          }))
                        }
                        className="active:opacity-80"
                      >
                        <HStack className="justify-between">
                          <Text className="font-semibold">{section.title}</Text>
                          <Ionicons
                            name={expanded ? 'chevron-up' : 'chevron-down'}
                            size={18}
                            color="#6B7280"
                          />
                        </HStack>
                      </Pressable>

                      {expanded ? (
                        <Input
                          value={session.soapNote[section.key]}
                          onChangeText={(value) => updateSoapSection(session.id, section.key, value)}
                          multiline
                          numberOfLines={6}
                          className="min-h-28"
                          textAlignVertical="top"
                        />
                      ) : null}
                    </VStack>
                  </CardContent>
                </Card>
              );
            })}
          </VStack>
        </Section>

        <Section
          title="Medication Recommendation Copilot"
          subtitle="Recommendation only. Requires explicit doctor validation before finalization."
        >
          <Card className="border border-warning/30">
            <CardContent>
              <Text className="text-sm text-warning">
                Safety Notice: Suggestions are supportive only and must be clinically validated by a doctor.
              </Text>
            </CardContent>
          </Card>

          <VStack gap="sm">
            {session.medicationRecommendations.map((recommendation) => (
              <Card key={recommendation.id}>
                <CardContent>
                  <VStack gap="sm">
                    <VStack gap="xs">
                      <Text className="font-semibold">{recommendation.name}</Text>
                      <Text className="text-sm text-text-secondary">
                        {recommendation.dosage} • {recommendation.frequency}
                      </Text>
                      <Text className="text-sm text-text-secondary">
                        Age group: {recommendation.ageGroup}
                      </Text>
                    </VStack>

                    <Text className="text-sm text-text-secondary">
                      Rationale: {recommendation.rationale}
                    </Text>
                    <Text className="text-sm text-warning">Safety: {recommendation.safetyNotes}</Text>

                    <HorizontalScroll>
                      {recommendationStates.map((status) => (
                        <Button
                          key={status}
                          size="sm"
                          variant={recommendation.status === status ? 'primary' : 'secondary'}
                          onPress={() => updateRecommendationStatus(session.id, recommendation.id, status)}
                        >
                          {status.replace('_', ' ')}
                        </Button>
                      ))}
                    </HorizontalScroll>

                    <Button
                      size="sm"
                      variant={recommendation.validatedByDoctor ? 'primary' : 'secondary'}
                      disabled={!isDoctor}
                      onPress={() =>
                        toggleDoctorValidation(
                          session.id,
                          recommendation.id,
                          !recommendation.validatedByDoctor,
                        )
                      }
                    >
                      {recommendation.validatedByDoctor
                        ? 'Doctor Validation Confirmed'
                        : 'Confirm Doctor Validation'}
                    </Button>
                  </VStack>
                </CardContent>
              </Card>
            ))}
          </VStack>
        </Section>

        <Section title="Post-Visit Documentation" subtitle="Generated from approved SOAP and validated medications.">
          <VStack gap="sm">
            <Button variant="secondary" size="sm" onPress={() => regeneratePostVisitSummary(session.id)}>
              Regenerate Summary
            </Button>
            <Input
              value={session.postVisitSummary?.content ?? ''}
              onChangeText={(value) => updatePostVisitSummary(session.id, value)}
              multiline
              numberOfLines={6}
              className="min-h-28"
              textAlignVertical="top"
              placeholder="Summary appears here after approval or regeneration."
            />
          </VStack>
        </Section>

        {!isDoctor ? (
          <Card>
            <CardContent>
              <Text className="text-sm text-text-secondary">
                Only users with Doctor role can execute final Approve &amp; Save.
              </Text>
            </CardContent>
          </Card>
        ) : null}

        <Button disabled={!isDoctor || !allRecommendationsValidated} onPress={onApprove}>
          Approve &amp; Save
        </Button>

        {resultMessage ? (
          <View className="pb-6">
            <Text className="text-sm text-primary">{resultMessage}</Text>
          </View>
        ) : null}
      </VStack>
    </Container>
  );
}
