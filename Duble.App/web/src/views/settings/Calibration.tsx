// views/settings/Calibration.tsx — thresholds chosen by measurement rather than by feel.
//
// Three kinds of pair have a known answer: files identical byte for byte must come out at distance 0, colour
// variants of one garment are the hard case, and random pairs of different garments are the easy one. The
// charts show all three with the current thresholds drawn across them, so a threshold that sits in the wrong
// place is visible rather than argued about.
import { useState } from 'react';
import { useApp } from '../../app/AppState';
import { bridge, ErrorCode, errorCodeOf, messageOf } from '../../bridge/bridge';
import type { CalibrationReport, Distribution, Thresholds } from '../../bridge/contract';
import { useBridgeEvent } from '../../bridge/hooks';
import { Button } from '../../components/Button';
import { Histogram, type ThresholdMark } from '../../components/Histogram';
import { Icon } from '../../components/Icon';
import { useToast } from '../../components/Toast';
import { useI18n, useTranslate } from '../../i18n';

/** The four thresholds calibration has an opinion about; the rest it copies from what is in force. */
const PROPOSED: (keyof Thresholds)[] = [
  'geometryIdentical',
  'geometrySimilar',
  'textureHashDistance',
  'textureColorDistance',
];

export function Calibration() {
  const t = useTranslate();
  const { job } = useApp();
  const toast = useToast();
  const [report, setReport] = useState<CalibrationReport | null>(null);

  useBridgeEvent('calibrate.done', (done) => setReport(done.report));
  useBridgeEvent('project.opened', () => setReport(null));

  const running = job?.kind === 'calibration' && (job.state === 'start' || job.state === 'progress');

  const run = async () => {
    try {
      await bridge.call('calibrate.run');
    } catch (failure) {
      const code = errorCodeOf(failure);
      toast.warn(
        code === ErrorCode.Busy ? t('sources.busy') : code === ErrorCode.NotFound ? t('settings.calibNoData') : messageOf(failure),
      );
    }
  };

  return (
    <div className="th-block">
      <div className="th-head">
        <h3>{t('settings.calib')}</h3>
        <Button variant="primary" small icon="play" disabled={running} onClick={run}>
          {running ? t('settings.calibRunning') : t('settings.calibRun')}
        </Button>
      </div>
      <p className="help">{t('settings.calibHelp')}</p>
      {report && <Report report={report} />}
    </div>
  );
}

function Report({ report }: { report: CalibrationReport }) {
  const t = useTranslate();
  const { formatDate } = useI18n();
  const toast = useToast();

  const inForce = report.usedThresholds;
  const proposal = report.proposal;

  const twoPlaces = (value: number) => value.toFixed(2);
  const whole = (value: number) => String(Math.round(value));
  const onePlace = (value: number) => value.toFixed(1);

  const geometryMarks: ThresholdMark[] = [
    { value: proposal?.geometryIdentical, label: t('calib.thIdentical'), className: 'm-a' },
    { value: proposal?.geometrySimilar, label: t('calib.thSimilar'), className: 'm-b' },
  ];
  const hashMarks: ThresholdMark[] = [{ value: proposal?.textureHashDistance, label: t('calib.threshold'), className: 'm-a' }];
  const colourMarks: ThresholdMark[] = [{ value: proposal?.textureColorDistance, label: t('calib.threshold'), className: 'm-a' }];

  const differs = !!proposal && !!inForce && PROPOSED.some((key) => Number(proposal[key]) !== Number(inForce[key]));

  const apply = async () => {
    if (!proposal) return;
    try {
      const state = await bridge.call('project.settings.set', {
        thresholds: {
          geometryIdentical: proposal.geometryIdentical,
          geometrySimilar: proposal.geometrySimilar,
          textureHashDistance: proposal.textureHashDistance,
          textureColorDistance: proposal.textureColorDistance,
        },
      });
      toast.ok(state.comparing ? t('settings.thresholdSavedCompare') : t('settings.saved'));
    } catch (failure) {
      toast.error(messageOf(failure));
    }
  };

  return (
    <div>
      <p className="muted">
        {t('settings.calibSummary', {
          withGeometry: report.garmentsWithGeometry,
          tex: report.decodedTextures,
          when: formatDate(report.when),
        })}
      </p>

      <div className="calib-grid">
        <Chart title={t('calib.geoNearest')} distribution={report.geoNearestForeign} marks={geometryMarks} format={twoPlaces} tone="neg" />
        <Chart title={t('calib.geoSha')} distribution={report.geoSameFile} marks={geometryMarks} format={twoPlaces} tone="pos" />
        <Chart title={t('calib.geoSame')} distribution={report.geoSameHash} marks={geometryMarks} format={twoPlaces} tone="pos" />
        <Chart title={t('calib.phVariants')} distribution={report.hashVariants} marks={hashMarks} format={whole} tone="neg" />
        <Chart title={t('calib.phSha')} distribution={report.hashIdentical} marks={hashMarks} format={whole} tone="pos" />
        <Chart title={t('calib.phRandom')} distribution={report.hashRandom} marks={hashMarks} format={whole} tone="neg" />
        <Chart title={t('calib.colVariants')} distribution={report.colorVariants} marks={colourMarks} format={onePlace} tone="neg" />
        <Chart title={t('calib.colRandom')} distribution={report.colorRandom} marks={colourMarks} format={onePlace} tone="neg" />
      </div>

      {proposal && (
        <div className="calib-prop">
          <span>
            <Icon name="info" />{' '}
            {t('settings.calibProposal', {
              geo: twoPlaces(proposal.geometryIdentical),
              geo4: twoPlaces(proposal.geometrySimilar),
              ph: whole(proposal.textureHashDistance),
              colour: twoPlaces(proposal.textureColorDistance),
            })}
          </span>
          {differs ? (
            <Button small icon="check" onClick={apply}>
              {t('settings.calibUse')}
            </Button>
          ) : (
            <span className="badge ok">{t('settings.calibSame')}</span>
          )}
        </div>
      )}

      {report.geoSuspicious > 0 && <p className="help">{t('settings.calibSuspicious', { n: report.geoSuspicious })}</p>}
    </div>
  );
}

function Chart({
  title,
  distribution,
  marks,
  format,
  tone,
}: {
  title: string;
  distribution: Distribution | undefined;
  marks: readonly ThresholdMark[];
  format: (value: number) => string;
  tone: 'pos' | 'neg';
}) {
  const t = useTranslate();

  const summary = distribution?.n
    ? `${t('calib.n', { n: distribution.n })} · ${t('calib.pct', {
        p05: format(distribution.p05),
        p50: format(distribution.p50),
        p95: format(distribution.p95),
      })}`
    : t('settings.calibNoData');

  return (
    <div className="calib-card">
      <div className="calib-title">
        <b>{title}</b>
        <span className="faint">{summary}</span>
      </div>
      <Histogram
        distribution={distribution}
        marks={marks}
        format={format}
        tone={tone}
        emptyText={t('settings.calibNoData')}
      />
    </div>
  );
}
