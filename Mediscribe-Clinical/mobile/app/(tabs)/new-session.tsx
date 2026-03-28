import { useMemo, useState } from 'react';
import { useRouter } from 'expo-router';
import { View } from 'react-native';
import Ionicons from '@expo/vector-icons/Ionicons';
import { Container } from '@/components/ui/Container';
import { VStack } from '@/components/ui/VStack';
import { HStack } from '@/components/ui/HStack';
import { Section } from '@/components/ui/Section';
import { Text } from '@/components/ui/Text';
import { Input } from '@/components/ui/Input';
import { Card, CardContent } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { HorizontalScroll } from '@/components/ui/HorizontalScroll';
import { useMediScribe } from '@/features/mediscribe/context/mediscribe-context';

const waveformPattern = [10, 22, 16, 30, 18, 12, 24, 16, 11, 26, 19, 14];

export default function NewSessionScreen() {
  const router = useRouter();
  const { createSessionDraft } = useMediScribe();

  const [patientName, setPatientName] = useState('');
  const [age, setAge] = useState('');
  const [sex, setSex] = useState<'female' | 'male' | 'other'>('female');
  const [allergiesText, setAllergiesText] = useState('');
  const [ineffectiveText, setIneffectiveText] = useState('');
  const [doctorTranscript, setDoctorTranscript] = useState('');
  const [patientTranscript, setPatientTranscript] = useState('');
  const [isRecording, setIsRecording] = useState(false);
  const [saving, setSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const isValid = useMemo(
    () => patientName.trim().length > 1 && Number(age) > 0,
    [age, patientName],
  );

  const handleCreateSession = async () => {
    if (!isValid) {
      setErrorMessage('Please complete patient name and age before creating the session.');
      return;
    }

    setErrorMessage(null);
    setSaving(true);

    const nextSessionId = await createSessionDraft({
      patientName: patientName.trim(),
      age: Number(age),
      sex,
      allergies: allergiesText
        .split(',')
        .map((item) => item.trim())
        .filter(Boolean),
      ineffectiveMedications: ineffectiveText
        .split(',')
        .map((item) => item.trim())
        .filter(Boolean),
      transcriptDoctorText:
        doctorTranscript.trim() ||
        'Doctor consultation prompts captured from recording. Edit for clarity.',
      transcriptPatientText:
        patientTranscript.trim() ||
        'Patient response transcript captured from recording. Edit for completeness.',
    });

    setSaving(false);
    router.push(`/session/${nextSessionId}`);
  };

  return (
    <Container keyboard>
      <VStack gap="lg">
        <VStack gap="xs">
          <Text className="text-2xl font-bold">New Consultation Session</Text>
          <Text className="text-text-secondary">
            Capture patient context, record consultation, and prepare structured drafts.
          </Text>
        </VStack>

        <Section title="Patient Profile" subtitle="Required details for context-aware SOAP and medication drafts.">
          <Input
            value={patientName}
            onChangeText={setPatientName}
            placeholder="Patient full name"
          />
          <Input
            value={age}
            onChangeText={setAge}
            placeholder="Age"
            keyboardType="numeric"
          />

          <HorizontalScroll>
            <Button
              variant={sex === 'female' ? 'primary' : 'secondary'}
              size="sm"
              onPress={() => setSex('female')}
            >
              Female
            </Button>
            <Button
              variant={sex === 'male' ? 'primary' : 'secondary'}
              size="sm"
              onPress={() => setSex('male')}
            >
              Male
            </Button>
            <Button
              variant={sex === 'other' ? 'primary' : 'secondary'}
              size="sm"
              onPress={() => setSex('other')}
            >
              Other
            </Button>
          </HorizontalScroll>

          <Input
            value={allergiesText}
            onChangeText={setAllergiesText}
            placeholder="Allergies (comma-separated)"
          />
          <Input
            value={ineffectiveText}
            onChangeText={setIneffectiveText}
            placeholder="Previously ineffective medications (comma-separated)"
          />
        </Section>

        <Section
          title="Consultation Capture"
          subtitle="Recording UI with live waveform visualization placeholder."
        >
          <Card>
            <CardContent>
              <VStack gap="md">
                <Button
                  onPress={() => setIsRecording((previous) => !previous)}
                  variant={isRecording ? 'destructive' : 'primary'}
                >
                  {isRecording ? 'Stop Recording' : 'Start Recording'}
                </Button>

                <HStack className="justify-center gap-1">
                  {waveformPattern.map((height, index) => (
                    <View
                      key={`${height}-${index}`}
                      className={isRecording ? 'bg-primary' : 'bg-border-strong'}
                      style={{ width: 6, height, borderRadius: 999 }}
                    />
                  ))}
                </HStack>

                <Text className="text-sm text-text-secondary">
                  {isRecording
                    ? 'Recording in progress. Transcript draft updates after stop.'
                    : 'Recording stopped. Edit transcript below before generating drafts.'}
                </Text>
              </VStack>
            </CardContent>
          </Card>
        </Section>

        <Section title="Transcript Review" subtitle="Two-column speaker split for Doctor and Patient edits.">
          <HorizontalScroll>
            <Card className="w-[300px]">
              <CardContent>
                <VStack gap="sm">
                  <HStack>
                    <Ionicons name="medkit-outline" size={18} color="#007AFF" />
                    <Text className="font-semibold">Doctor</Text>
                  </HStack>
                  <Input
                    value={doctorTranscript}
                    onChangeText={setDoctorTranscript}
                    multiline
                    numberOfLines={6}
                    placeholder="Doctor transcript content"
                    className="min-h-36"
                    textAlignVertical="top"
                  />
                </VStack>
              </CardContent>
            </Card>

            <Card className="w-[300px]">
              <CardContent>
                <VStack gap="sm">
                  <HStack>
                    <Ionicons name="person-outline" size={18} color="#007AFF" />
                    <Text className="font-semibold">Patient</Text>
                  </HStack>
                  <Input
                    value={patientTranscript}
                    onChangeText={setPatientTranscript}
                    multiline
                    numberOfLines={6}
                    placeholder="Patient transcript content"
                    className="min-h-36"
                    textAlignVertical="top"
                  />
                </VStack>
              </CardContent>
            </Card>
          </HorizontalScroll>
        </Section>

        {errorMessage ? <Text className="text-sm text-error">{errorMessage}</Text> : null}

        <Button onPress={handleCreateSession} disabled={saving}>
          {saving ? 'Creating Session...' : 'Generate SOAP Draft & Continue'}
        </Button>
      </VStack>
    </Container>
  );
}
